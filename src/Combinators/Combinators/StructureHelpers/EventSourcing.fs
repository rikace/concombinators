module Combinators.StructureHelpers.EventSourcing

//[snippet:Core Abstractions]
type EventTransaction<'State, 'Event, 'Result> = 
    Transaction of (Envelope<'State> -> 'Result * 'Event[])

and Envelope<'State> =
    {
        StreamId : string
        Value : 'State
        Version : int64
    }

and ITransactionContext<'State, 'Event> =
    abstract MaxRetries : int option
    abstract GetState : streamId:string -> Async<Envelope<'State>>
    abstract TryCommit : streamId:string -> expectedVersion:int64 -> 'Event[] -> Async<bool>

// building a transaction instance from a standard apply/exec pair of functions
module EventTransaction =

    let fromApplyExec (apply : 'State -> 'Event -> 'State) (exec  : 'State -> #seq<'Event>) =
        Transaction(fun state ->
            let events = exec state.Value |> Seq.toArray
            let state' = Array.fold apply state.Value events
            state', events)

// schedule an optimistic transaction again a given context
let runTransaction 
    (context : ITransactionContext<'State, 'Event>) 
    (streamId : string) 
    (Transaction transaction) =

    let rec aux numRetries = async {
        if context.MaxRetries |> Option.exists (fun mr -> numRetries > mr) then 
            invalidOp "exceeded max number of retries!"

        let! state = context.GetState streamId
        let result, events = transaction state
        let! success = context.TryCommit streamId state.Version events
        if not success then return! aux (numRetries + 1)
        else
            let wrap i e = { StreamId = streamId ; Value = e ; Version = state.Version + int64 i + 1L }
            let eventEnvs = events |> Array.mapi wrap
            return result, eventEnvs
    }

    aux 0

//[/snippet]
//[snippet:Dummy In-Memory Context]

// Dummy In-memory implementation, replays the entire stream to recover an aggregate
type InMemoryEventStore<'State, 'Event>(init : 'State, apply : 'State -> 'Event -> 'State) =
    let eventStreams = System.Collections.Concurrent.ConcurrentDictionary<string, ResizeArray<'Event>>()
    let getStream streamId = eventStreams.GetOrAdd(streamId, fun _ -> ResizeArray())
    interface ITransactionContext<'State, 'Event> with
        member __.MaxRetries = None
        member __.GetState streamId = async {
            let eventStream = getStream streamId
            let events = lock eventStream (fun () -> eventStream.ToArray())
            let state = Array.fold apply init events
            return { StreamId = streamId ; Value = state ; Version = events.LongLength - 1L }
        }

        member __.TryCommit streamId expectedVersion events = async {
            let eventStream = getStream streamId
            return lock eventStream (fun () ->
                if expectedVersion <> int64 eventStream.Count - 1L then false
                else
                    eventStream.AddRange events ; true)
        }

//[/snippet]
//[snippet:Example: Event Sourced Stack]

type Stack = Stack of int list
type Command = Push of int | Pop
type Event = Pushed of int | Popped of int

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Stack =
    let empty = Stack []
    let apply s e =
        match s, e with
        | Stack s , Pushed i -> Stack(i :: s)
        | Stack [], Popped _ -> s
        | Stack (h :: t), Popped _ -> Stack t

    let exec cmd s =
        match cmd, s with
        | Push i, _ -> [Pushed i]
        | Pop, Stack [] -> []
        | Pop, Stack (h::_) -> [Popped h]

    let cmd2Transaction cmd = EventTransaction.fromApplyExec apply (exec cmd)


let context = InMemoryEventStore(Stack.empty, Stack.apply)

runTransaction context "foo" (Push 1 |> Stack.cmd2Transaction) |> Async.RunSynchronously
runTransaction context "foo" (Push 2 |> Stack.cmd2Transaction) |> Async.RunSynchronously
runTransaction context "foo" (Pop |> Stack.cmd2Transaction) |> Async.RunSynchronously

// running a custom transaction
let incrHead = 
    Transaction
        (function
        | { Value = Stack [] } -> None, [||]
        | { Value = Stack (h :: _) } -> Some h, [|Popped h ; Pushed (h + 1)|])

runTransaction context "bar" incrHead |> Async.RunSynchronously
runTransaction context "bar" (Push 0 |> Stack.cmd2Transaction) |> Async.RunSynchronously
runTransaction context "bar" incrHead |> Async.RunSynchronously
runTransaction context "bar" incrHead |> Async.RunSynchronously

// contented transactions
[ for i in 1 .. 10 -> Push i ]
|> Seq.map (runTransaction context "bar" << Stack.cmd2Transaction)
|> Seq.map (fun j -> async { let! _ = Async.Sleep 2 in return! j })
|> Async.Parallel
|> Async.RunSynchronously
//[/snippet]