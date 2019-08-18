module Combinators.Future


open System
open System.Threading
open System.Threading.Tasks
let rnd = Random()
let t1 = Task.Run(fun () -> printfn "Thread #1 %d : - %A" Thread.CurrentThread.ManagedThreadId DateTime.Now) 
let t2 = fun () -> Task.Run(fun () -> printfn "Thread #1 %d : - %A" Thread.CurrentThread.ManagedThreadId DateTime.Now)

t2()


type Future<'T> = 
  abstract Start : ('T -> unit) -> unit
  

//type Future<'T> = ('T -> unit) -> unit

module Future = 
  /// A computation that immediately returns the given value
  let unit v = 
    { new Future<_> with
        member x.Start(f) = f v }

  /// Start the computation and do nothing when it finishes.
  let start (a:Future<_>) =
    a.Start(fun () -> ())

  let bind (f:'a -> Future<'b>) (a:Future<'a>) : Future<'b> = 
    { new Future<'b> with
        member x.Start(g) =
          a.Start(fun a ->
            let ab = f a
            ab.Start(g) ) }
    
  /// Defines a computation builed for asyncs.
  /// For and While are defined in terms of bind and unit.
  type FutureBuilder() = 
    member x.Return(v) = unit v
    member x.Bind(a, f) = bind f a
    member x.Zero() = unit ()

    member x.For(vals, f) =
      match vals with
      | [] -> unit ()
      | v::vs -> f v |> bind (fun () -> x.For(vs, f))

    member x.Delay(f:unit -> Future<_>) =
      { new Future<_> with
          member x.Start(h) = f().Start(h) }

    member x.While(c, f) = 
      if not (c ()) then unit ()
      else f |> bind (fun () -> x.While(c, f))

    member x.ReturnFrom(a) = a
          
let future = Future.FutureBuilder()


//[<Sealed>]
//type Future() =
//    static let self = Future()
//
//    member inline this.Return(x: 'T) : Future<'T> =
//        fun f -> f x
//    member inline this.ReturnFrom(x: Future<'T>) = x
//    member inline this.Bind
//        (x: Future<'T1>, f: 'T1 -> Future<'T2>) : Future<'T2> =
//        fun k -> x (fun v -> f v k)
//    static member inline Start(x: Future<unit>) =
//        // Pool.Spawn(fun () -> x ignore)
//        TaskPool.Add(fun () -> x ignore)
//        
//    static member inline RunSynchronously(x: Future<'T>) : 'T =
//        let res = ref Unchecked.defaultof<_>
//        let sem = new System.Threading.SemaphoreSlim(0)
//        Pool.Spawn(fun () ->
//            x (fun v ->
//                res := v
//                ignore (sem.Release())))
//        sem.Wait()
//        !res
//    static member inline FromContinuations
//        (f : ('T -> unit) *
//             (exn -> unit) *
//             (System.OperationCanceledException -> unit)
//                -> unit) : Future<'T> =
//        fun k -> f (k, ignore, ignore)

  
module TaskX =
    type Future<'a> = ('a -> unit) -> unit
    
    let inline run (f:'a -> unit) v =
        fun () -> Task.Run(new Action(fun () -> f v))

        
let exec (f: unit -> Task) = f()         
        
let t4 = TaskX.run (fun () -> printfn "Thread #1 %d : - %A" Thread.CurrentThread.ManagedThreadId DateTime.Now)
let t5 = TaskX.run (fun i -> printfn "val %d - Thread #1 %d : - %A" (i + 1) Thread.CurrentThread.ManagedThreadId DateTime.Now)

t4() |> exec |> ignore

(t5 41) |> exec |> ignore
    
    
        
let test (f : Task) =
   fun () -> f

let t3 = test (Task.Run(fun () -> printfn "Thread #1 %d : - %A" Thread.CurrentThread.ManagedThreadId DateTime.Now))
t3()