
#load "TaskBuilder.fs"
#load "Samples.fsx"

open System
open System.Collections
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks.V2

module SerivceCurrencyOne =
    let getRate (ccy : string ) = Task.FromResult 0
    
module SerivceCurrencyTwo =    
    let getRate (ccy : string ) = Task.FromResult 0
    
    
// The interesting part is combining the two calls to GetRate. For this kind of task, you can use the OrElse function,
// which takes a task and a fallback to use in case the task fails”


let orElse (fallBack : unit -> Task<'a>) (op : Task<'a>) = task {
    let! k = op.ContinueWith(fun (t : Task<'a>) ->
        if t.Status = TaskStatus.Faulted then
            fallBack()
        else Task.FromResult(t.Result))
    return! k
    }

let ccy = ""

let r = SerivceCurrencyOne.getRate(ccy) |> orElse (fun () -> SerivceCurrencyTwo.getRate ccy)

let recover (fallback : exn -> 'a) (op : Task<'a>) = task{
    return! op.ContinueWith(fun (t : Task<'a>) ->
        if t.Status = TaskStatus.Faulted then
            fallback(t.Exception)
        else t.Result)
}


let getRate =
    SerivceCurrencyOne.getRate ""
    |> Task.map (fun rate -> printfn "The rate is %d" rate)
    |> recover (fun ex -> printfn "Error : %s" ex.Message)
    
    
let bimap (successed : 'a -> 'b) (faulted : exn -> 'b) (op : Task<'a>) = task {
    return! op.ContinueWith(fun (t : Task<'a>) ->
        if t.Status = TaskStatus.Faulted then
            faulted t.Exception
        else successed t.Result)    
    }

let getRate =
    SerivceCurrencyOne.getRate ""
    |> bimap (fun rate -> printfn "The rate is %d" rate)
             (fun ex -> printfn "Error : %s" ex.Message)
             
             
[<HttpGet("translate/{amount}/{from}/{to}")>]
let translate (amount : decimal, from : string, to : string) = task {
    let ccy = from + to
    return! 
        SerivceCurrencyOne.getRate(ccy)
        |> orElse (fun () -> SerivceCurrencyTwo.getRate ccy)
        |> Task.map (fun rate -> amount * rate)
        |> Task.bimap (fun result -> StatusCode.Ok result)
                      (fun ex -> StatusCode(500, ex))
    
}

let rec retry (retries :int) (delayMillis : int) (op : unit -> Task<_>) = task {
    match retries with
    | 0 -> return! op()
    | n -> return! op() |> orElse (fun () ->
                task {
                    do! Task.Delay delayMillis
                    return! retry (n - 1) delayMillis op
                })
    }


(fun () -> SerivceCurrencyOne.getRate ccy) |> retry 3 1000




module TaskParallel =
    
    type Flight = { airline : string; price : decimal }
    
    type Airline =
        abstract flights : string -> string -> DateTime -> Task<Flight seq>
        abstract bestFare : string -> string -> DateTime -> Task<Flight>


    let delta = Unchecked.defaultof<Airline>
    let americans = Unchecked.defaultof<Airline>
    
    let bestFareM (from : string) (to' : string) (on : DateTime) = task {
        let! d = delta.bestFare from to' on
        let! a = americans.bestFare from to' on
        return
            if d.price > a.price then a else d
        
    }
            
    let singleton value = value |> Task.FromResult

    let bind (f : 'a -> Task<'b>) (x : Task<'a>) = task {
          let! x = x
          return! f x
      }

    let apply x f =
      bind (fun f' ->
        bind (fun x' -> singleton(f' x')) x) f
      
      
    
    let pickCheaper a b = 
        if a.price > b.price then b else a
        
    let bestFareA (from : string) (to' : string) (on : DateTime) = task {
        return! singleton pickCheaper
        |> apply (delta.bestFare from to' on)
        |> apply (americans.bestFare from to' on)
                
    }    
    
    
    
    
    /// Map a Result producing function over a list to get a new Result 
    /// using applicative style
    /// ('a -> Result<'b>) -> 'a list -> Result<'b list>
    let singleton value = value |> Task.FromResult

    let bind (f : 'a -> Task<'b>) (x : Task<'a>) = task {
          let! x = x
          return! f x
      }

    let apply f x =
        bind (fun f' ->
          bind (fun x' -> singleton(f' x')) x) f
        
    let map f x = x |> bind (f >> singleton)

    let map2 f x y =
        (apply (apply (singleton f) x) y)

    let map3 f x y z =
        apply (map2 f x y) z        

    
    /// Map a Result producing function over a list to get a new Task 
    /// using monadic style
    /// ('a -> Result<'b>) -> 'a list -> Result<'b list>
    let rec traverseTasktM f list =

        // define the monadic functions
        let (>>=) x f = bind f x
        let retn = singleton

        // define a "cons" function
        let cons head tail = head :: tail

        // loop through the list
        match list with
        | [] -> 
            // if empty, lift [] to a Result
            retn []
        | head::tail ->
            // otherwise lift the head to a Result using f
            // then lift the tail to a Result using traverse
            // then cons the head and tail and return it
            f head >>= (fun h -> 
                traverseTasktM f tail >>= (fun t ->
                    retn (cons h t) ))
            
            
    let rec traverseTaskA (list : 'a list) f = task {

        // define the applicative functions
        let (<*>) = apply
        let retn = singleton

        // define a "cons" function
        let cons head tail = head :: tail

        // loop through the list
        match list with
        | [] -> 
            // if empty, lift [] to a Result
            return! retn []
        | head::tail ->
            // otherwise lift the head to a Result using f
            // and cons it with the lifted version of the remaining list
            return! retn cons <*> (f head) <*> (traverseTaskA tail f)
    }
    
    
    // Notice that I’ve called the function TraverseM (for monadic) because the implementation is monadic:
    // if one item fails validation, the validation function won’t be called for any of the subsequent items.”

    
    let airlines = Unchecked.defaultof<Airline seq>
    
    let map f x = x |> bind (f >> singleton)
    
    // Seq<Task<Seq<Flight>>>
    let flights from to' on = airlines |> Seq.map (fun a -> a.flights from to' on)
    
    
    
    let search (airlines : Airline list) from to' on = task {
        let! flights = traverseTaskA airlines (fun a -> a.flights from to' on)
        return flights |> Seq.collect id |> Seq.sortByDescending (fun p -> p.price)
    }

    let searchA (airlines : Airline list) from to' on = task {
        let! flights = traverseTaskA airlines (fun a ->
            a.flights from to' on 
            |> recover (fun ex -> Seq.empty))
        return flights |> Seq.collect id |> Seq.sortByDescending (fun p -> p.price)
    }        
    
    
module PuralyParallel =
    
    let parMap (data : 'a list) (projection : 'a -> 'b) = 0
    
    let sum (ints : int list) = (ints, 0) ||> Seq.foldBack(fun item state -> item + state)
    
    let rec sumRec (ints : int list) =
        match ints with
        | [] -> 0
        | lst ->
            let left, right = ints |> List.splitAt (lst.Length / 2)
            sumRec left + sumRec right
            
            
    // this is blocking
    // what's the problem 
    let rec sumRec depth (ints : int list) =
        match ints with
        | [] -> 0
        | lst ->
            let left, right = ints |> List.splitAt (lst.Length / 2)
            //sumRec left + sumRec right
            if depth < 0 then 
                let left  = sumRec depth left  
                let right = sumRec depth right   
                left + right
            else
                let left  = Task.Run(fun () -> sumRec (depth - 1) left) 
                let right = Task.Run(fun () -> sumRec (depth - 1) right)  
                left.Result + right.Result
    
    
    let singleton value = value |> Task.FromResult

    let bind (f : 'a -> Task<'b>) (x : Task<'a>) = task {
          let! x = x
          return! f x
      }

    let apply f x = 
        bind (fun f' ->
          bind (fun x' -> singleton(f' x')) x) f    
    
    let map2 f x y =
        (apply (apply (singleton f) x) y)
        
    let apply of' ox =
        map2 (fun f x -> f x) of' ox        
        
    let map3 f x y z =
        apply (map2 f x y) z

    let map4 f x y z w =
        apply (map3 f x y z) w
        
    // map2 (+) (singleton 1) (singleton 2)
    
    let fork (f : Task<'a>) = 
        fun () -> task { return! f  }    
        
    let lazySingleton (a : 'a) = singleton a |> fork
                
        
        // THIS IS NOT BLOCKING USE CONTInuation
    let rec sumRec depth (ints : int list) = task {
        match ints with
        | [] -> return 0
        | lst ->
            let left, right = ints |> List.splitAt (lst.Length / 2)
            if depth < 0 then 
                let! left  = sumRec depth left  
                let! right = sumRec depth right   
                return left + right
            else
                let left  = sumRec (depth - 1) left 
                let right = sumRec (depth - 1) right  
                return! map2 (+) left right }
    
    
    
    let taskCPS (op : Task<'a>) (f: 'a -> Task<'b>) = task {
        return! op.ContinueWith(fun (t : Task<'a>) ->
             f t.Result)
    }
            

    // Message type used by the agent - contains queueing 
    // of work items and notification of completion 
    type internal ThrottlingAgentMessage<'a> =
      | Completed of 'a
      | Work of Async<'a>
      | Progress of 'a
        
        
    (*
    Agent that can be used for controlling the number of concurrently executing asynchronous workflows.
    The agent runs a specified number of operations concurrently and queues remaining pending requests.
    The queued work items are started as soon as one of the previous items completes.
    *)    
        
    let map (degree : int) (lst : Async<'a> list) (f : 'a -> 'a) = 
        /// Represents an agent that runs operations in concurrently. When the number
        /// of concurrent operations exceeds 'degree', they are queued and processed later
        let agent = MailboxProcessor.Start(fun agent -> 

            /// Represents a state when the agent is blocked
            let rec waiting state index = 
              // Use 'Scan' to wait for completion of some work
              agent.Scan(function
                | Progress res -> Some(working (degree - 1) index (Some(f state res)))
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
      
      


        
//    
//    let rec sumRec depth (ints : int list) = task {
//        match ints with
//        | [] -> return fun () -> 0
//        | lst ->
//            let left, right = ints |> List.splitAt (lst.Length / 2)
//            if depth < 0 then 
//                let! left  = sumRec depth left  
//                let! right = sumRec depth right   
//                return left + right
//            else
//                let left  = sumRec (depth - 1) left |> fork 
//                let right = sumRec (depth - 1) right |> fork  
//                return! map2 (+) left right }    
//    
    
//    let rec quicksortParallelWithDepth depth aList =    // #A
//    match aList with
//    | [] -> []
//    | firstElement :: restOfList ->
//        let smaller, larger =
//            List.partition (fun number -> number > firstElement) restOfList
//        if depth < 0 then   // #B
//            let left  = quicksortParallelWithDepth depth smaller  //#C
//            let right = quicksortParallelWithDepth depth larger   //#C
//            left @ (firstElement :: right)
//        else
//            let left  = Task.Run(fun () -> quicksortParallelWithDepth (depth - 1) smaller) // #D
//            let right = Task.Run(fun () -> quicksortParallelWithDepth (depth - 1) larger)  // #D
//            left.Result @ (firstElement :: right.Result)
    
    