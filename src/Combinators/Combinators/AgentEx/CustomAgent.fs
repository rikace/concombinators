module Combinators.AgentEx.CustomAgent

open System
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent


 
type 'a ISharedActor =
   abstract Post : msg:'a -> unit
   abstract PostAndReply : msgFactory:(('b -> unit) -> 'a) -> 'b
 
type 'a SharedMailbox() =
   let msgs = ConcurrentQueue()
   let mutable isStarted = false
   let mutable msgCount = 0
   let mutable react = Unchecked.defaultof<_>
   let mutable currentMessage = Unchecked.defaultof<_>
 
   let rec execute(isFirst) =
 
      let inline consumeAndLoop() =
         react currentMessage
         currentMessage <- Unchecked.defaultof<_>
         let newCount = Interlocked.Decrement &msgCount
         if newCount = 0 then execute false
 
      if isFirst then consumeAndLoop()
      else
         let hasMessage = msgs.TryDequeue(&currentMessage)
         if hasMessage then consumeAndLoop()
         else 
            Thread.SpinWait 20
            execute false
 
   member __.Receive(callback) = 
      isStarted <- true
      react <- callback
 
   member __.Post msg =
      while not isStarted do Thread.SpinWait 20
      let newCount = Interlocked.Increment &msgCount
      if newCount = 1 then
         currentMessage <- msg
         // Might want to schedule this call on another thread.
         execute true
      else msgs.Enqueue msg
 
   member __.PostAndReply msgFactory =
      let value = ref Unchecked.defaultof<_>
      use onReply = new AutoResetEvent(false)
      let msg = msgFactory (fun x ->
         value := x
         onReply.Set() |> ignore
      )
      __.Post msg
      onReply.WaitOne() |> ignore
      !value
 
 
   interface 'a ISharedActor with
      member __.Post msg = __.Post msg
      member __.PostAndReply msgFactory = __.PostAndReply msgFactory
 
module SharedActor =
   let Start f =
      let mailbox = new SharedMailbox<_>()
      f mailbox
      mailbox :> _ ISharedActor

module Test = 
   type CounterMsg =
      | Add of int64
      | GetAndReset of (int64 -> unit)


   let sharedActor = 
      SharedActor.Start(fun inbox ->  
         let rec loop count =
            inbox.Receive(fun msg ->
               match msg with
               | Add n -> loop (count + n)
               | GetAndReset reply ->
                  reply count
                  loop 0L)
         loop 0L)

   sharedActor.Post (Add 8L)
   let v = sharedActor.PostAndReply(fun ch -> GetAndReset(ch))
   printfn "%d" v