module Combinators.AgentEx.AgentPipe

module AgentPipe = 
    open System
    open System.Threading
    open System.Threading.Tasks

    /// Message type used by the agent - contains queueing
    /// of work items and notification of completion
    type JobRequest<'T, 'R> =
        | Ask of 'T * AsyncReplyChannel<'R>
        | Completed
        | Quit
    
    /// Represents an agent that runs operations in concurrently. When the number
    /// of concurrent operations exceeds 'limit', they are queued and processed later
    type PipeAgent<'T, 'R>(limit, operation:'T -> Async<'R>) =
        let jobCompleted = new Event<'R>()
    
        let agent = Agent<JobRequest<'T, 'R>>.Start(fun agent ->
            let dispose() = (agent :> IDisposable).Dispose()
        /// Represents a state when the agent is working
            let rec running jobCount = async {
          // Receive any message
              let! msg = agent.Receive()
              match msg with
              | Quit -> dispose()
              | Completed ->
              // Decrement the counter of work items
                  return! running (jobCount - 1)
              // Start the work item & continue in blocked/working state
              | Ask(job, reply) ->
                   do!
                     async { try
                                 let! result = operation job
                                 jobCompleted.Trigger result
                                 reply.Reply(result)
                             finally agent.Post(Completed) }
                   |> Async.StartChild 
                   |> Async.Ignore
                   if jobCount < limit - 1 then return! running (jobCount + 1)
                   else return! idle ()
        /// Represents a state when the agent is blocked
                }
            and idle () =
          // Use 'Scan' to wait for completion of some work
                  agent.Scan(function
                  | Completed -> Some(running (limit - 1))
                  | _ -> None)
        // Start in working state with zero running work items
            running 0)
    
      /// Queue the specified asynchronous workflow for processing
        member x.Ask(job) = agent.PostAndAsyncReply(fun ch -> Ask(job, ch))
    
        member x.Subsrcibe(action) = jobCompleted.Publish |> Observable.subscribe(action)
    
    module AgentCombinators = 
    
        let pipelineAgent l f =
            let a = PipeAgent(l, f)
            fun x -> a.Ask(x)
        
        let retn x = async { return x }
        
        let bind f xAsync = async {
            let! x = xAsync
            return! f x }
            
        let (>>=) x f = bind f x // async.Bind(x, f)    
        let pipeline agent1 agent2 x = retn x >>= agent1 >>= agent2
        
    type Replyable<'a, 'b> = | Reply of 'a * AsyncReplyChannel<'b>

    let pipelined (agent:MailboxProcessor<_>) previous =
        async {
            let! result = agent.PostAndTryAsyncReply(fun rc -> Reply(previous, rc))
            match result with
            | Some(result) ->
                match result with
                | Choice1Of2(result) -> return result
                | Choice2Of2(err) -> return raise(err)
            | None -> return failwithf "Stage timed out"
        }    
    
    
    type IAgent<'Input, 'Output> =
      abstract Post : 'Input -> unit
      abstract Output : IEvent<'Output>

    
    module ConsumerProducer =
            
        type Producer() =
            let outputEvent = Event<string>()
    
            let agent =
                MailboxProcessor.Start(fun inbox ->
                    let rec loop state =
                        async { let! msg = inbox.Receive()
                                printfn "rec P %O" msg
                                outputEvent.Trigger (string msg)
                                return! loop (msg::state)
                        }
                    loop [])
            
            interface IAgent<int, string> with
                member __.Post msg = agent.Post msg
                member __.Output = outputEvent.Publish
    
        type Consumer() =
            let outputEvent = Event<bool>()
    
            let agent =
                MailboxProcessor.Start(fun inbox ->
                    let rec loop state =
                        async { let! msg = inbox.Receive()
                                printfn "rec C %O" msg
                                outputEvent.Trigger true
                                return! loop (msg::state)
                        }
                    loop [])
            
            interface IAgent<string, bool> with
                member __.Post msg = agent.Post msg
                member __.Output = outputEvent.Publish
    
        let pipe (producer: IAgent<_, _>) (consumer: IAgent<_, _>) =
          producer.Output.Add consumer.Post            
    
        let p = Producer()
        let c = Consumer()
    
        let run = pipe p c      
    
        (p :> IAgent<_,_>).Post(1)