module Combinators.AgentEx.AgentMB

open System
open System.Collections.Generic

module BoundedMbCond =
  type BoundedMbReq<'a> =
  | Put of 'a * AsyncReplyChannel<unit>
  | Take of AsyncReplyChannel<'a>
  | GetAll of AsyncReplyChannel<'a[]>

  
  type IBoundedMbCond<'a> =
    abstract member Add : 'a -> unit
    abstract member Remove : 'a -> unit
    abstract member Reset : unit -> unit
    abstract member Satisfied : bool
    abstract member WillSatisfy : 'a -> bool
    
  /// A mailbox bounded by a condition.
  type BoundedMb<'a> internal (cond:IBoundedMbCond<'a>) =
  
    let agent = MailboxProcessor.Start <| fun agent ->
  
      let queue = Queue<_>()
  
      let rec loop () = async {
        match queue.Count with
        | 0 -> do! tryReceive ()
        | _ ->
          if cond.Satisfied then
            do! trySend ()
          else
            do! trySendOrReceive ()
        return! loop () }
  
      and tryReceive () = 
        agent.Scan (function
          | Put (a,rep) -> Some (receive(a,rep))
          | GetAll rep -> Some (getAll rep)
          | _ -> None)
  
      and receive (a:'a, rep:AsyncReplyChannel<unit>) = async {
        queue.Enqueue a
        cond.Add a
        rep.Reply () }
  
      and trySend () = 
        agent.Scan (function
          | Take rep -> Some (send rep)
          | GetAll rep -> Some (getAll rep)
          | _ -> None)
  
      and send (rep:AsyncReplyChannel<'a>) = async {
        let a = queue.Dequeue ()
        cond.Remove a
        rep.Reply a }
  
      and getAll (rep:AsyncReplyChannel<'a[]>) = async {
        let xs = queue.ToArray ()
        for x in xs do
          cond.Remove x
        rep.Reply xs }
  
      and trySendOrReceive () = async {
        let! msg = agent.Receive ()
        match msg with
        | GetAll rep -> return! getAll rep
        | Put (a,rep) -> return! receive (a,rep)
        | Take rep -> return! send rep }
  
      loop ()
  
    member __.Put (a:'a) =
      agent.PostAndAsyncReply (fun ch -> Put (a,ch))
  
    member __.Take () =
      agent.PostAndAsyncReply (fun ch -> Take ch)
  
    member __.GetAll () =
      agent.PostAndAsyncReply (fun ch -> GetAll ch)
  
    interface IDisposable with
      member __.Dispose () = (agent :> IDisposable).Dispose()
