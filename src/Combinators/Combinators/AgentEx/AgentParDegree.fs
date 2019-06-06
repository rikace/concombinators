module Combinators.AgentEx.ParDegree

    open System
    open System.Collections.Generic

    open System.Threading

    let par (degree: int) (f: 'a -> Async<'b>) : 'a -> Async<'b> =
        let agent = MailboxProcessor.Start <| fun mailbox -> 
            async {        
                use semaphore = new SemaphoreSlim(degree)
                let rec loop () = 
                    async {
                        let! (x: 'a, rep: AsyncReplyChannel<'b>) = mailbox.Receive()
                        do! semaphore.WaitAsync() |> Async.AwaitTask
                        async {
                            try
                                let! r = f x
                                rep.Reply r
                            finally
                                semaphore.Release() |> ignore
                        } |> Async.Start
                        return! loop () 
                    }
                return! loop () 
            }
        fun x -> agent.PostAndAsyncReply(fun repCh -> x, repCh)

    type MapAgentMessage<'a, 'b> =
        | Progress of 'b
        | Work of Async<'a>

    let map (degree : int) (lst : Async<'a> list) (f : 'a -> 'b -> 'c) = 
        /// Represents an agent that runs operations in concurrently. When the number
        /// of concurrent operations exceeds 'degree', they are queued and processed later
        let agent = MailboxProcessor.Start(fun agent -> 

            /// Represents a state when the agent is blocked
            let rec waiting state index = 
              // Use 'Scan' to wait for completion of some work
              agent.Scan(function
                | Progress res -> Some(working (degree - 1) index (state |> Option.map(fun s -> f s res)))
                | _ -> None)

            /// Represents a state when the agent is working
            and working count index state = async { 
              // Receive any message 
              let! msg = agent.Receive()
              match msg with 
              | Progress res -> 
                  // Decrement the counter of work items
                  let newState =
                      match state with
                      | None -> Some res
                      | Some s -> f s res |> Some
                  
                  return! working (count - 1) (index + 1) newState
              | Work work ->
                  // Start the work item & continue in blocked/working state
                  async { let! result = work
                          agent.Post (Progress result) }
                  |> Async.Start
                  if count < degree - 1 then return! working (count + 1) (index + 1) state
                  else return! waiting state index }

            // Start in working state with zero running work items
            working 0 0 None)      
        
        lst |> List.iter (fun work -> agent.Post(Work work))

    let mapReduce (map    : 'T1 -> Async<'T2>)
                  (reduce : 'T2 -> 'T2 -> Async<'T2>)
                  (input  : seq<'T1>) : Async<'T2> =
        let run (a: Async<'T>) (k: 'T -> unit) =
            Async.StartWithContinuations(a, k, ignore, ignore)
        Async.FromContinuations <| fun (ok, _, _) ->
            let k = ref 0
            let agent =
                new MailboxProcessor<_>(fun chan ->
                    async {
                        for i in 2 .. k.Value do
                            let! x = chan.Receive()
                            let! y = chan.Receive()
                            return run (reduce x y) chan.Post
                        let! r = chan.Receive()
                        return ok r
                    })
            k :=
                (0, input)
                ||> Seq.fold (fun count x ->
                    run (map x) agent.Post
                    count + 1)
            agent.Start()