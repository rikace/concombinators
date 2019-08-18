module Channels.Channels

(*
    The function callcc (call with current continuation) calls a function with the current continuation (which is the function return’s continuation) as an argument.
*)
let callcc (f: ('a -> Async<'b>) -> Async<'a>) : Async<'a> =
  Async.FromContinuations(fun (cont, econt, ccont) ->
    Async.StartWithContinuations(f (fun a -> Async.FromContinuations(fun (_, _, _) -> cont a)), cont, econt, ccont))
    
    
(*
    RPC
    IntraProc   PInvoke
    
    structure MailboxMulticast :> MULTICAST =
    
  val mbox = MBox.mailbox ()  

  val bufferChannel : unit -> 'a buffer_chan
  val send : ('a buffer_chan * 'a) -> unit
  val recvEvt : 'a buffer_chan -> 'a CML.event
  
  
signature MULTICAST =
sig
  type 'a mchan
  type 'a port

  val mChannel : unit -> 'a mchan
  val port : 'a mchan -> 'a port
  val multicast : ('a mchan * 'a) -> unit
  val recvEvt : 'a port -> 'a CML.event
  
  receive :: Channel a -> Event a
  wrap :: Event a -> (a -> IO b) -> Event b
  choose :: [Event a] -> Event a
  guard :: IO (Event a) -> Event a
  sync :: Event a -> IO a
  
end


  
  
  structure Q = Fifo

  datatype 'a buffer_chan =
    BC of {
      getCh : 'a chan,
      putCh : 'a chan
    }

  fun bufferChannel () =
    let
      val getCh = channel ()
      val putCh = channel ()

      fun loop q =
        if Q.isEmpty q
        then loop (Q.enqueue (q, recv putCh))
        else select [
          wrap (recvEvt putCh, fn a => Q.enqueue (q, a)),
          wrap (sendEvt (getCh, Q.head q), fn () => loop (#1 (Q.dequeue q)))
        ]
    in
      spawn (fn () => ignore (loop Q.empty))
    ; BC { getCh = getCh, putCh = putCh }
    end

  fun send (BC { putCh, ... }, a) = CML.send (putCh, a)

  fun recvEvt (BC
  
    
*)    
//    let putString = Channel<string>("puts")
//    let putInt = Channel<int>("puti")
//    let get = SyncChannel<string>("get")
//
//    join {
//      match! get, putString, putInt with
//      | repl, v, ? -> return react { yield repl.Reply("Echo " + v) }
//      | repl, ?, v -> return react { yield repl.Reply("Echo " + (string v)) } 
//    }

//Val rech :  ‘a chan -> ‘a
//Val send : (‘a chan * ‘a) -> unit
//Cell : ‘a -> ‘a cell
//Get: ‘a cell -> ‘a
//Put : (‘a cell * ‘a) -> unit


//The buffer can be called using F# asynchronous workflows as follows:
//
//    // Put 5 values to 'putString' and 5 values to 'putInt'
//    for i in 1 .. 5 do 
//      putString.Call("Hello!")
//      putInt.Call(i)
//
//    // Repeatedly call 'get' to read the next value. This is a blocking
//    // operation, so it should be done from asynchronous workflow to 
//    // avoid blocking physical threads.
//    async { 
//      while true do
//        let! repl = get.AsyncCall()
//        printfn "got: %s" repl }
//    |> Async.Start

module Channel =

    open System
    open System.Collections.Generic
    open System.Collections.Concurrent
    open System.Threading.Tasks
    open System.Linq
    open System.Threading
        
    type Agent<'a> = MailboxProcessor<'a>
    
    type AgentDisposable<'T>(f:MailboxProcessor<'T> -> Async<unit>,
                                ?cancelToken:System.Threading.CancellationTokenSource) =
        let cancelToken = defaultArg cancelToken (new CancellationTokenSource())
        let agent = MailboxProcessor.Start(f, cancelToken.Token)

        member x.Agent = agent
        interface IDisposable with
            member x.Dispose() = (agent :> IDisposable).Dispose()
                                 cancelToken.Cancel()

    type AgentDisposable<'T> with
        member inline this.withSupervisor (supervisor: Agent<exn>, transform) =
            this.Agent.Error.Add(fun error -> supervisor.Post(transform(error))); this

        member this.withSupervisor (supervisor: Agent<exn>) =
            this.Agent.Error.Add(supervisor.Post); this    
    
    type MailboxProcessor<'a> with
        static member public parallelWorker (workers:int, behavior:MailboxProcessor<'a> -> Async<unit>, ?errorHandler, ?cancelToken:CancellationTokenSource) =
            let cancelToken = defaultArg cancelToken (new System.Threading.CancellationTokenSource())
            let thisletCancelToken = cancelToken.Token
            let errorHandler = defaultArg errorHandler ignore
            let supervisor = Agent<System.Exception>.Start(fun inbox -> async {
                                while true do
                                    let! error = inbox.Receive()
                                    errorHandler error })
            let agent = new MailboxProcessor<'a>((fun inbox ->
                let agents = Array.init workers (fun _ ->
                    (new AgentDisposable<'a>(behavior, cancelToken))
                        .withSupervisor supervisor )
                thisletCancelToken.Register(fun () ->
                    agents |> Array.iter(fun agent -> (agent :> IDisposable).Dispose())
                ) |> ignore
                let rec loop i = async {
                    let! msg = inbox.Receive()
                    agents.[i].Agent.Post(msg)
                    return! loop((i+1) % workers)
                }
                loop 0), thisletCancelToken)
            agent.Start()
            agent

    // ChannelAgent for CSP implementation using MailboxProcessor
    type private Context = {cont:unit -> unit; context:ExecutionContext}

    type TaskPool private (numWorkers) =
        let worker (inbox: MailboxProcessor<Context>) =
            let rec loop() = async {
                let! ctx = inbox.Receive()
                let ec = ctx.context.CreateCopy()
                ExecutionContext.Run(ec, (fun _ -> ctx.cont()), null)
                return! loop() }
            loop()
        let agent = MailboxProcessor<Context>.parallelWorker(numWorkers, worker)

        static let self = TaskPool(2)
        member private this.Add continutaion =
            let ctx = {cont = continutaion; context = ExecutionContext.Capture() }
            agent.Post(ctx)
        static member Spawn (continuation:unit -> unit) = self.Add continuation

    //Listing ChannelAgent for CSP implementation using MailboxProcessor
    type internal ChannelMsg<'a> =
        | Recv of ('a -> unit) * AsyncReplyChannel<unit>
        | Send of 'a * (unit -> unit) * AsyncReplyChannel<unit>

    type [<Sealed>] ChannelAgent<'a>() =
        let agent = MailboxProcessor<ChannelMsg<'a>>.Start(fun inbox ->
            let readers = Queue<'a -> unit>()
            let writers = Queue<'a * (unit -> unit)>()

            let rec loop() = async {
                let! msg = inbox.Receive()
                match msg with
                | Recv(ok , reply) ->
                    if writers.Count = 0 then
                        readers.Enqueue ok
                        reply.Reply( () )
                    else
                        let (value, cont) = writers.Dequeue()
                        TaskPool.Spawn  cont
                        reply.Reply( (ok value) )
                    return! loop()
                | Send(x, ok, reply) ->
                    if readers.Count = 0 then
                        writers.Enqueue(x, ok)
                        reply.Reply( () )
                    else
                        let cont = readers.Dequeue()
                        TaskPool.Spawn ok
                        reply.Reply( (cont x) )
                    return! loop() }
            loop())

        member this.Recv(ok: 'a -> unit)  =
            agent.PostAndAsyncReply(fun ch -> Recv(ok, ch)) |> Async.Ignore

        member this.Send(value: 'a, ok:unit -> unit)  =
            agent.PostAndAsyncReply(fun ch -> Send(value, ok, ch)) |> Async.Ignore

        member this.Recv() =
            // TODO
            // callcc (fun ok -> agent.PostAndAsyncReply(fun ch -> Recv(ok, ch))) //|> Async.RunSynchronously)

            Async.FromContinuations(fun (ok, _,_) ->
                agent.PostAndAsyncReply(fun ch -> Recv(ok, ch)) |> Async.RunSynchronously)

        member this.Send (value:'a) =
            Async.FromContinuations(fun (ok, _,_) ->
                agent.PostAndAsyncReply(fun ch -> Send(value, ok, ch)) |> Async.RunSynchronously )

    let run (action:Async<_>) = action |> Async.Ignore |> Async.Start

    let rec subscribe (chan:ChannelAgent<_>) (handler:'a -> unit) =
        chan.Recv(fun value -> handler value
                               subscribe chan handler) |> run