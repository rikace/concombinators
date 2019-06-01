module Combinators.AgentEx.ThrottlingAgent

open System
open System.Threading
open System.Threading.Tasks
open System.Threading.Tasks.Dataflow
open System.Collections.Generic


// We can ask the agent to enqueue a new work item;
// and the agent sends itself a completed notification
type ThrottlingMessage =
  | Enqueue of Async<unit>
  | Completed

type ThrottlingAgent(limit) =
    let throttlingAgent =
        MailboxProcessor.Start(fun inbox -> async {
          // The agent body is not executing in parallel,
          // so we can safely use mutable queue & counter
          let queue = System.Collections.Generic.Queue<_>()
          let running = ref 0
          while true do
            // Enqueue new work items or decrement the counter
            // of how many tasks are running in the background
            let! msg = inbox.Receive()
            match msg with
            | Completed -> decr running
            | Enqueue w -> queue.Enqueue(w)
            // If we have less than limit & there is some work to
            // do, then start the work in the background!
            while running.Value < limit && queue.Count > 0 do
              let work = queue.Dequeue()
              incr running
              do!
                // When the work completes, send 'Completed'
                // back to the agent to free a slot
                async { do! work
                        inbox.Post(Completed) }
                |> Async.StartChild
                |> Async.Ignore })

    member __.DoWork(v:Async<unit>) = throttlingAgent.Post(Enqueue v)