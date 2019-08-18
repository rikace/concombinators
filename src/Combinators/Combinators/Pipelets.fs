module Combinators.Pipelets


open System
open System.Threading
open System.Reflection
    
[<Interface>]
type IPipeletInput<'a> =
    /// Posts a message to the pipelet input.
    abstract Post: 'a -> unit

/// A wrapper for pipeline payloads.
type Message<'a, 'b> =
    | Payload of 'a
    | Attach of 'b IPipeletInput
    | Detach of 'b IPipeletInput

/// A pipelet is a named, write-only agent that processes incoming messages then publishes the results to the next pipeline stage(s).
type Pipelet<'a, 'b>(name:string, transform:'a -> 'b seq, router:'b seq -> 'b IPipeletInput seq -> unit, maxcount, maxwait:int, ?overflow, ?errors) = 

    let disposed = ref false
    let ss = new SemaphoreSlim(maxcount, maxcount)
    let overflow = defaultArg overflow (fun x -> printf "%A Overflow: %A" DateTime.Now.TimeOfDay x)
    let errors = defaultArg errors (fun (e:Exception) -> printf "%A Processing error: %A" DateTime.Now.TimeOfDay e.Message)

    let dispose disposing =
        if not !disposed then
            if disposing then ss.Dispose()
            disposed := true
        
    let mailbox = MailboxProcessor.Start(fun inbox ->
        let rec loop routes = async {
            let! msg = inbox.Receive()
            match msg with
            | Payload(data) ->
                ss.Release() |> ignore
                try
                    data |> transform |> router <| routes
                    return! loop routes
                with //force loop resume on error
                | ex -> errors ex
                        return! loop routes
            | Attach(stage) -> return! loop (stage::routes)
            | Detach(stage) -> return! loop (List.filter (fun x -> x <> stage) routes)
        }
        loop [])

    let post data =
        if ss.Wait(maxwait) then
            mailbox.Post(Payload data)
        else overflow data

    /// Gets the name of the pipelet.
    member this.Name with get() = name

    /// Attaches a subsequent stage.
    member this.Attach(stage) = mailbox.Post(Attach stage)
        
    /// Detaches a subsequent stage.
    member this.Detach (stage) = mailbox.Post(Detach stage)

    /// Posts a message to the pipelet agent.
    member this.Post(data) = post data

    interface IPipeletInput<'a> with
        /// Posts a message to the pipelet input.
        member this.Post(data) = post data

    interface IDisposable with
        /// Disposes the pipelet, along with the encapsulated SemaphoreSlim.
        member this.Dispose() =
            dispose true
            GC.SuppressFinalize(this)

let inline (<--) (m:Pipelet<_,_>) msg = (m :> IPipeletInput<_>).Post(msg)
let inline (-->) msg (m:Pipelet<_,_>)= (m :> IPipeletInput<_>).Post(msg)

let inline (++>) (a:Pipelet<_,_>) b = a.Attach(b);b
let inline (-+>) (a:Pipelet<_,_>) b = a.Detach(b);b

/// Picks a circular sequence of routes that repeats i.e. A,B,C,A,B,C etc
let roundRobin<'a> =
    let makeSeqSkipper =
        let tmnt = ref 0
        let tick(seq) =
            tmnt := (!tmnt + 1) % (Seq.length seq)
            Seq.take 1 <| Seq.skip !tmnt seq 
        tick

    let createRoundRobin messages (routes) =
        if routes |> Seq.isEmpty then ()
        else 
            let route =  makeSeqSkipper routes
            messages |> Seq.iter (fun msg -> do route |> Seq.iter (fun (s:'a IPipeletInput) -> s.Post msg) )
    createRoundRobin

/// Simply picks the first route
let basicRouter messages (routes:'a IPipeletInput seq) =
    if routes |> Seq.isEmpty then ()
    else let route = routes |> Seq.head in messages |> Seq.iter (fun msg -> do route.Post msg)

//sends the message to all attached routes
let multicastRouter messages (routes:'a IPipeletInput seq) =
    if routes |> Seq.isEmpty then ()
    else messages |> Seq.iter (fun msg -> do routes |> Seq.iter (fun (s:'a IPipeletInput) -> s.Post msg) )
    
    
module PipeletsDemo =

    open System
    open System.Diagnostics
    open System.Threading
    

    let reverse (s:string) = 
        String(s |> Seq.toArray |> Array.rev)

    let oneToSingleton a b f=
        let result = b |> f 
        result |> Seq.singleton

    /// Total number to run through test cycle
    let number = 1000000

    /// To Record when we are done
    let counter = ref 0
    let sw = new Stopwatch()
    let countThis (a:String) =
        do Interlocked.Increment(counter) |> ignore
        if !counter % number = 0 then 
            sw.Stop()
            printfn "Execution time: %A" sw.Elapsed.TotalMilliseconds
            printfn "Items input: %d" number
            printfn "Time per item: %A ms (Elapsed Time / Number of items)" 
                (TimeSpan.FromTicks(sw.Elapsed.Ticks / int64 number).TotalMilliseconds)
            printfn "Press a key to exit."
        counter |> Seq.singleton

    let OneToSeqRev a b = 
        //printfn "stage: %s item: %s" a b
        //Async.RunSynchronously(Async.Sleep(500))
        oneToSingleton a b reverse 

    let generateCircularSeq (s) = 
        let rec next () = 
            seq {
                for element in s do
                    yield element
                yield! next()
            }
        next()

    let stage1 = new Pipelet<_,_>("Stage1", OneToSeqRev "1", roundRobin, number, -1)
    let stage2 = new Pipelet<_,_>("Stage2", OneToSeqRev "2", basicRouter, number, -1)
    let stage3 = new Pipelet<_,_>("Stage3", OneToSeqRev "3", basicRouter, number, -1)
    let stage4 = new Pipelet<_,_>("Stage4", OneToSeqRev "4", basicRouter, number, -1)
    let stage5 = new Pipelet<_,_>("Stage5", OneToSeqRev "5", basicRouter, number, -1)
    let stage6 = new Pipelet<_,_>("Stage6", OneToSeqRev "6", basicRouter, number, -1)
    let stage7 = new Pipelet<_,_>("Stage7", OneToSeqRev "7", basicRouter, number, -1)
    let stage8 = new Pipelet<_,_>("Stage8", OneToSeqRev "8", basicRouter, number, -1)
    let stage9 = new Pipelet<_,_>("Stage9", OneToSeqRev "9", basicRouter, number, -1)
    let stage10 = new Pipelet<_,_>("Stage10", OneToSeqRev "10", basicRouter, number, -1)
    let final = new Pipelet<_,_>("Final", countThis, basicRouter, number, -1)

    stage1 ++> stage2 ++> final|> ignore
    stage1 ++> stage3 ++> final |> ignore
    stage1 ++> stage4 ++> final |> ignore
    stage1 ++> stage5 ++> final |> ignore
    stage1 ++> stage6 ++> final |> ignore
    stage1 ++> stage7 ++> final |> ignore
    stage1 ++> stage8 ++> final |> ignore
    stage1 ++> stage9 ++> final |> ignore
    stage1 ++> stage10 ++> final |> ignore

    
    sw.Start()
    for str in ["John"; "Paul"; "George"; "Ringo"; "Nord"; "Bert"] 
    |> generateCircularSeq 
    |> Seq.take number
        do  str --> stage1

    