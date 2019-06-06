module Combinators.AsyncEx.AsyncArrow

open System
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent
open Combinators.AsyncEx.AsyncCombinators

// A function which produces an async computation as output.
//type AsyncArrow<'a, 'b> = 'a -> Async<'b>
type AsyncArrow<'a,'b> = AsyncArrow of ('a -> Async<'b>)

type AsyncFilter<'a, 'b, 'c, 'd> = AsyncArrow<'a, 'b> -> AsyncArrow<'c, 'd>

type AsyncFilter<'a, 'b> = AsyncArrow<'a, 'b> -> AsyncArrow<'a, 'b>

type AsyncSink<'a> = AsyncArrow<'a, unit>

module AsyncArrow =
    
    // The async arrow type can have many interpretations and is particularly well suited
    // at representing an async request-reply protocol. Data driven services are usually a composition
    // of various request-reply interactions and it can be useful to reify the type.
    // The supported operations would be all those of arrows as seen here but also many others specific to Async
    // and other types. Consider operations such as:

    let ofAsyncArrow (AsyncArrow x) = x

    let (>>=) m f = Async.bind f m

    let retn x = Async.retn x
    
    // (f:'a -> Async<'b>) = AsyncArrow f
    let arr (f:'a -> Async<'b>) = AsyncArrow f
    
    // f:('a -> 'b) -> AsyncArrow<'a,'b>
    let pure' (f:'a -> 'b) = arr (f >> retn)
    
    // AsyncArrow<'a,'b> -> AsyncArrow<'b,'c> -> AsyncArrow<'a,'c>
    let (>>>>) (AsyncArrow f) (AsyncArrow g) = arr ((fun m -> m >>= g) << f)
    
    // m:AsyncArrow<'a,'b> -> f:('b -> 'c) -> AsyncArrow<'a,'c>
    let mapArrow m f = m >>>> pure' f
    
    // AsyncArrow<unit,'a> -> Async<'a>
    let toMonad (AsyncArrow f) = f()
    
    // m:Async<'a> -> f:('a -> 'b) -> Async<'b>
    let map m f = mapArrow (arr (fun _ -> m)) f |> toMonad
    
    // m:Async<'a> -> f:('a -> Async<'b>) -> Async<'b>
    let bind m f = (arr (fun _ -> m) >>>> arr f) |> toMonad
    
    let inline lift (f:'a -> 'b) : AsyncArrow<'a, 'b> = AsyncArrow (f >> async.Return)
    
    // f:('a -> 'b) -> m:Async<'a> -> Async<'b>
    let lift2 f m = m >>= (fun a -> retn (f a))
    
    let identity<'a> : AsyncArrow<'a, 'a> = lift id    
    
    /// Creates an async function which first invokes async function f then g with the output of f.
    let inline compose (g:AsyncArrow<'b, 'c>) (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'c> =
        fun a -> (ofAsyncArrow f) a |> Async.bind (ofAsyncArrow g)
        |> arr

//    let inline compose (g:AsyncArrow<'b, 'c>) (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'c> =
//        fun a ->
//            (ofAsyncArrow f) a |> Async.bind (ofAsyncArrow g) //AsyncArrow

//    let maplAsync (f:AsyncArrow<'c, 'a>) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'c, 'b> =
//        f >> ofAsyncArrow >> Async.bind a
//  let inline compose (g:AsyncFunc<'b, 'c>) (f:AsyncFunc<'a, 'b>)  : AsyncFunc<'a, 'c> =
//    fun a -> f a |> Async.bind g

    let inline catch (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, Choice<'b, exn>> =
        ofAsyncArrow f >> Async.Catch |> AsyncArrow

    let mergeAllPar (ss:seq<AsyncSink<'a>>) =
        fun a -> ss |> Seq.map (fun x -> x |> a) |> Async.Parallel |> Async.Ignore

    let mapOut (f:'b -> 'c) (a:'a -> Async<'b>) : 'a -> Async<'c> =
        a >> Async.map f

    let mapOutAsync (f:'b -> Async<'c>) (a:'a -> Async<'b>) : 'a -> Async<'c> =
        a >> Async.bind f

    let mapIn (f:'a2 -> 'a) (a:'a -> Async<'b>) : 'a2 -> Async<'b> =
        f >> a

    let after (f:'a * 'b -> _) (g:'a -> Async<'b>) : 'a -> Async<'b> =
        fun a -> g a |> Async.map (fun b -> let _ = f (a,b) in b)

    let composeAsync (f:'a -> Async<'b>) (g:'b -> Async<'c>) : 'a -> Async<'c> =
      fun a ->
        let b = f a // first evaluate f at a
        async.Bind(b, g) // then bind the result using async function g
        
    /// Maps over the input to an async function.
    let mapl (f:'a2 -> 'a) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a2, 'b> =
        f >> (ofAsyncArrow a) |> arr
    
    /// Maps over the input to an async function.
    let maplAsync (f:AsyncArrow<'c, 'a>) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'c, 'b> =
        (ofAsyncArrow f) >> Async.bind (ofAsyncArrow a) |> arr
    
    /// Maps over the output of an async function.
    let mapr (f:'b -> 'c) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'c> =
        (ofAsyncArrow a) >> Async.map f |> arr
    
    /// Maps over the output of an async function.
    let maprAsync (f:AsyncArrow<'b, 'c>) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'c> =
        (ofAsyncArrow a) >> Async.bind (ofAsyncArrow f) |> arr 
    
    /// Maps over both the input and the output of an async function.
    let dimap (f:'c -> 'a) (g:'b -> 'd) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'c, 'd> =
        f >> (ofAsyncArrow a) >> Async.map g |> arr
    
    /// Maps over both the input and the output of an async function.
    let dimapAsync (f:AsyncArrow<'c, 'a>) (g:AsyncArrow<'b, 'd>) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'c, 'd> =
        (ofAsyncArrow f) >> Async.bind (ofAsyncArrow a) >> Async.bind (ofAsyncArrow g) |> arr    



module AsyncSink =

  let ars (f:'a -> Async<unit>) : AsyncSink<'a> = AsyncArrow.arr f  
    
  // Maps a function over the input of a sink.
  let contramap (f:'b -> 'a) (s:AsyncSink<'a>) : AsyncSink<'b> =
    AsyncArrow.mapl f s

  /// Maps a function over the input of a sink.
  let contramapAsync (f:'b -> Async<'a>) (s:AsyncSink<'a>) : AsyncSink<'b> =
    AsyncArrow.maplAsync (AsyncArrow.arr f) s

  /// Merges two sinks such that the resulting sink invokes the first one, then the second one.
  let merge (s1:AsyncSink<'a>) (s2:AsyncSink<'a>) : AsyncSink<'a> =
    (fun a -> (AsyncArrow.ofAsyncArrow s1) a |> Async.bind (fun _ -> (AsyncArrow.ofAsyncArrow s2) a))
    |> AsyncArrow.arr
    

  /// Merges two sinks such that the resulting sink invokes the first one, then the second one.
  /// This merge with arguments flipped.
  let inline andThen s2 s1 = merge s1 s2

  /// Creates a sink which invokes the underlying sinks sequentially.
  let mergeAll (xs:seq<AsyncSink<'a>>) : AsyncSink<'a> =
    fun a -> async {
      for s in xs do
        do! (AsyncArrow.ofAsyncArrow s) a }
    |> AsyncArrow.arr

  /// Creates a sink which invokes the underlying sinks in parallel.
  let mergeAllPar (xs:seq<AsyncSink<'a>>) : AsyncSink<'a> =
    fun a -> xs |> Seq.map (fun x -> (AsyncArrow.ofAsyncArrow x) a) |> Async.Parallel |> Async.Ignore
    |> AsyncArrow.arr
    
  /// Merges two sinks such that the resulting sink invokes the first one, then the second one.
  let mergePar (s1:AsyncSink<'a>) (s2:AsyncSink<'a>) : AsyncSink<'a> =
    mergeAllPar [ s1 ; s2 ]

  /// Lifts an async sink to operate on sequences of the input type in parallel.
  let seqPar (s:AsyncSink<'a>) : AsyncSink<seq<'a>> =
    Seq.map (AsyncArrow.ofAsyncArrow s) >> Async.Parallel >> Async.Ignore
    |> AsyncArrow.arr

  /// Lifts an async sink to operate on sequences of the input type sequetially.
  let seq (s:AsyncSink<'a>) : AsyncSink<seq<'a>> =
    fun xs -> async { for x in xs do do! (AsyncArrow.ofAsyncArrow s) x }
    |> AsyncArrow.arr