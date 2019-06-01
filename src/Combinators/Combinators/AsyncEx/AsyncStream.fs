module Combinators.AsyncEx.AsyncStream

// [snippet:Declaration of asynchronous sequence]
open System.IO

/// Represents a sequence of values 'T where items 
/// are generated asynchronously on-demand
type AsyncSeq<'T> = Async<AsyncSeqInner<'T>> 
and AsyncSeqInner<'T> =
  | Ended
  | Item of 'T * AsyncSeq<'T>
// [/snippet]

// [snippet:Using asynchronous sequences]
/// Read file 'fn' in blocks of size 'size'
/// (returns on-demand asynchronous sequence)
let readInBlocks fn size = async {
  let stream = File.OpenRead(fn)
  let buffer = Array.zeroCreate size
  
  /// Returns next block as 'Item' of async seq
  let rec nextBlock() = async {
    let! count = stream.AsyncRead(buffer, 0, size)
    if count = 0 then return Ended
    else 
      // Create buffer with the right size
      let res = 
        if count = size then buffer
        else buffer |> Seq.take count |> Array.ofSeq
      return Item(res, nextBlock()) }

  return! nextBlock() }


An asynchronous sequence is similar to the seq type, but the elements of the sequence are generated asynchronously without blocking the caller as in Async. This snippet declares asynchronous sequence and uses it to compare two files in 1k block


/// Asynchronous function that compares two asynchronous sequences
/// item by item. If an item doesn't match, 'false' is returned
/// immediately without generating the rest of the sequence. If the
/// lengths don't match, exception is thrown.
let rec compareAsyncSeqs seq1 seq2 = async {
  let! item1 = seq1
  let! item2 = seq2
  match item1, item2 with 
  | Item(b1, ns1), Item(b2, ns2) when b1 <> b2 -> return false
  | Item(b1, ns1), Item(b2, ns2) -> return! compareAsyncSeqs ns1 ns2
  | Ended, Ended -> return true
  | _ -> return failwith "Size doesn't match" }

/// Compare two files using 1k blocks
let s1 = readInBlocks "f1" 1000
let s2 = readInBlocks "f2" 1000
compareAsyncSeqs s1 s2
// [/snippet]



module AsyncSeqEx =
    
  open System
  open System.Diagnostics
  open System.Collections.Generic
  open System.Collections.Concurrent
  open System.Threading
  open System.Threading.Tasks
  open System.Runtime.ExceptionServices
  open System.Diagnostics
  open Combinators.AsyncEx
  
  type AsyncStream<'a> = Async<AsyncStreamNode<'a>>
  /// A node of an async stream consisting of an element and the rest of the stream.
  and AsyncStreamNode<'a> = ASN of 'a * AsyncStream<'a>
  
  /// An asynchronous sequence is a computation that asynchronously produces 
  /// a next cell of a linked list - the next cell can be either empty (Nil)
  /// or it can be a value, followed by another asynchronous sequence.
  //type AsyncSeq<'T> = Async<AsyncSeqRes<'T>>
  //and AsyncSeqRes<'T> = 
  //  | Nil
  //  | Cons of 'T * AsyncSeq<'T>
  
  
  /// An asynchronous sequence represents a delayed computation that can be
  /// started to produce either Cons value consisting of the next element of the
  /// sequence (head) together with the next asynchronous sequence (tail) or a
  /// special value representing the end of the sequence (Nil)
  type AsyncSeq<'T> = Async<AsyncSeqInner<'T>>
  
  /// The interanl type that represents a value returned as a result of
  /// evaluating a step of an asynchronous sequence
  and AsyncSeqInner<'T> =
      | Nil
      | Cons of 'T * AsyncSeq<'T>
      
      
  /// Iterate over the whole asynchronous sequence as fast as
  /// possible and run the specified function `f` for each value.
  let rec run f (aseq:AsyncSeq<_>) = async { 
      let! next = aseq
      match next with
      | Nil -> return ()
      | Cons(v, vs) -> 
          f v 
          return! run f vs }
  
  /// A function that iterates over an asynchronous sequence 
  /// and returns a new async sequence with transformed values
  let rec map f (aseq:AsyncSeq<'T>) : AsyncSeq<'R> = async { 
      let! next = aseq
      match next with
      | Nil -> return Nil
      | Cons(v, vs) -> return Cons(f v, map f vs) }  
      
  
  /// Module with helper functions for working with asynchronous sequences
  [<RequireQualifiedAccess>]
  module AsyncSeq =
  
    /// Creates an empty asynchronou sequence that immediately ends
    [<GeneralizableValue>]
    let empty<'T> : AsyncSeq<'T> =
      async { return Nil }
  
    /// Creates an asynchronous sequence that generates a single element and then ends
    let singleton (v:'T) : AsyncSeq<'T> =
      async { return Cons(v, empty) }
  
    /// Yields all elements of the first asynchronous sequence and then
    /// all elements of the second asynchronous sequence.
    let rec append (seq1: AsyncSeq<'T>) (seq2: AsyncSeq<'T>) : AsyncSeq<'T> =
      async { let! v1 = seq1
              match v1 with
              | Nil -> return! seq2
              | Cons (h,t) -> return Cons(h,append t seq2) }
  
  
    /// Computation builder that allows creating of asynchronous
    /// sequences using the 'asyncSeq { ... }' syntax
    type AsyncSeqBuilder() =
      member x.Yield(v) = singleton v
      member x.Return(()) = empty
      member x.YieldFrom(s) = s
      member x.Zero () = empty
      member x.Bind (inp:Async<'T>, body : 'T -> AsyncSeq<'U>) : AsyncSeq<'U> =
        async.Bind(inp, body)
      member x.Combine (seq1:AsyncSeq<'T>,seq2:AsyncSeq<'T>) =
        append seq1 seq2
      member x.While (gd, seq:AsyncSeq<'T>) =
        if gd() then x.Combine(seq,x.Delay(fun () -> x.While (gd, seq))) else x.Zero()
      member x.Delay (f:unit -> AsyncSeq<'T>) =
        async.Delay(f)
  
    /// Builds an asynchronou sequence using the computation builder syntax
    let asyncSeq = new AsyncSeqBuilder()
  
    /// Tries to get the next element of an asynchronous sequence
    /// and returns either the value or an exception
    let internal tryNext (input:AsyncSeq<_>) = async {
      try
        let! v = input
        return Choice1Of2 v
      with e ->
        return Choice2Of2 e }
  
    /// Implements the 'TryWith' functionality for computation builder
    let rec internal tryWith (input : AsyncSeq<'T>) handler =  asyncSeq {
      let! v = tryNext input
      match v with
      | Choice1Of2 Nil -> ()
      | Choice1Of2 (Cons (h, t)) ->
          yield h
          yield! tryWith t handler
      | Choice2Of2 rest ->
          yield! handler rest }
  
    /// Implements the 'TryFinally' functionality for computation builder
    let rec internal tryFinally (input : AsyncSeq<'T>) compensation = asyncSeq {
        let! v = tryNext input
        match v with
        | Choice1Of2 Nil ->
            compensation()
        | Choice1Of2 (Cons (h, t)) ->
            yield h
            yield! tryFinally t compensation
        | Choice2Of2 e ->
            compensation()
            yield! raise e }
  
    /// Creates an asynchronou sequence that iterates over the given input sequence.
    /// For every input element, it calls the the specified function and iterates
    /// over all elements generated by that asynchronous sequence.
    /// This is the 'bind' operation of the computation expression (exposed using
    /// the 'for' keyword in asyncSeq computation).
    let rec collect f (input : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
        let! v = input
        match v with
        | Nil -> ()
        | Cons(h, t) ->
            yield! f h
            yield! collect f t }
  
  
    // Add additional methods to the 'asyncSeq' computation builder
    type AsyncSeqBuilder with
  
      member x.TryFinally (body: AsyncSeq<'T>, compensation) =
        tryFinally body compensation
  
      member x.TryWith (body: AsyncSeq<_>, handler: (exn -> AsyncSeq<_>)) =
        tryWith body handler
  
      member x.Using (resource:#IDisposable, binder) =
        tryFinally (binder resource) (fun () ->
          if box resource <> null then resource.Dispose())
  
      /// For loop that iterates over a synchronous sequence (and generates
      /// all elements generated by the asynchronous body)
      member x.For(seq:seq<'T>, action:'T -> AsyncSeq<'TResult>) =
        let enum = seq.GetEnumerator()
        x.TryFinally(x.While((fun () -> enum.MoveNext()), x.Delay(fun () ->
          action enum.Current)), (fun () ->
            if enum <> null then enum.Dispose() ))
  
      /// Asynchronous for loop - for all elements from the input sequence,
      /// generate all elements produced by the body (asynchronously). See
      /// also the AsyncSeq.collect function.
      member x.For (seq:AsyncSeq<'T>, action:'T -> AsyncSeq<'TResult>) =
        collect action seq
  
  
    // Add asynchronous for loop to the 'async' computation builder
    type Microsoft.FSharp.Control.AsyncBuilder with
      member x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) =
        async.Bind(seq, function
          | Nil -> async.Zero()
          | Cons(h, t) -> async.Combine(action h, x.For(t, action)))
  
    // --------------------------------------------------------------------------
    // Additional combinators (implemented as async/asyncSeq computations)
  
    /// Builds a new asynchronous sequence whose elements are generated by
    /// applying the specified function to all elements of the input sequence.
    ///
    /// The specified function is asynchronous (and the input sequence will
    /// be asked for the next element after the processing of an element completes).
    let mapAsync f (input : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
      for itm in input do
        let! v = f itm
        yield v }
  
    /// Asynchronously iterates over the input sequence and generates 'x' for
    /// every input element for which the specified asynchronous function
    /// returned 'Some(x)'
    ///
    /// The specified function is asynchronous (and the input sequence will
    /// be asked for the next element after the processing of an element completes).
    let chooseAsync f (input : AsyncSeq<'T>) : AsyncSeq<'R> = asyncSeq {
      for itm in input do
        let! v = f itm
        match v with
        | Some v -> yield v
        | _ -> () }
  
    /// Builds a new asynchronous sequence whose elements are those from the
    /// input sequence for which the specified function returned true.
    ///
    /// The specified function is asynchronous (and the input sequence will
    /// be asked for the next element after the processing of an element completes).
    let filterAsync f (input : AsyncSeq<'T>) = asyncSeq {
      for v in input do
        let! b = f v
        if b then yield v }
  
    /// Asynchronously returns the last element that was generated by the
    /// given asynchronous sequence (or the specified default value).
    let rec lastOrDefault def (input : AsyncSeq<'T>) = async {
      let! v = input
      match v with
      | Nil -> return def
      | Cons(h, t) -> return! lastOrDefault h t }
  
    /// Asynchronously returns the first element that was generated by the
    /// given asynchronous sequence (or the specified default value).
    let firstOrDefault def (input : AsyncSeq<'T>) = async {
      let! v = input
      match v with
      | Nil -> return def
      | Cons(h, _) -> return h }
  
    /// Aggregates the elements of the input asynchronous sequence using the
    /// specified 'aggregation' function. The result is an asynchronous
    /// sequence of intermediate aggregation result.
    ///
    /// The aggregation function is asynchronous (and the input sequence will
    /// be asked for the next element after the processing of an element completes).
    let rec scanAsync f (state:'TState) (input : AsyncSeq<'T>) = asyncSeq {
      let! v = input
      match v with
      | Nil -> ()
      | Cons(h, t) ->
          let! v = f state h
          yield v
          yield! t |> scanAsync f v }
  
    /// Iterates over the input sequence and calls the specified function for
    /// every value (to perform some side-effect asynchronously).
    ///
    /// The specified function is asynchronous (and the input sequence will
    /// be asked for the next element after the processing of an element completes).
    let rec iterAsync f (input : AsyncSeq<'T>) = async {
      for itm in input do
        do! f itm }
  
    /// Returns an asynchronous sequence that returns pairs containing an element
    /// from the input sequence and its predecessor. Empty sequence is returned for
    /// singleton input sequence.
    let rec pairwise (input : AsyncSeq<'T>) = asyncSeq {
      let! v = input
      match v with
      | Nil -> ()
      | Cons(h, t) ->
          let prev = ref h
          for v in t do
            yield (!prev, v)
            prev := v }
  
    /// Aggregates the elements of the input asynchronous sequence using the
    /// specified 'aggregation' function. The result is an asynchronous
    /// workflow that returns the final result.
    ///
    /// The aggregation function is asynchronous (and the input sequence will
    /// be asked for the next element after the processing of an element completes).
    let rec foldAsync f (state:'TState) (input : AsyncSeq<'T>) =
      input |> scanAsync f state |> lastOrDefault state
  
    /// Same as AsyncSeq.foldAsync, but the specified function is synchronous
    /// and returns the result of aggregation immediately.
    let rec fold f (state:'TState) (input : AsyncSeq<'T>) =
      foldAsync (fun st v -> f st v |> async.Return) state input
  
    /// Same as AsyncSeq.scanAsync, but the specified function is synchronous
    /// and returns the result of aggregation immediately.
    let rec scan f (state:'TState) (input : AsyncSeq<'T>) =
      scanAsync (fun st v -> f st v |> async.Return) state input
  
    /// Same as AsyncSeq.mapAsync, but the specified function is synchronous
    /// and returns the result of projection immediately.
    let map f (input : AsyncSeq<'T>) =
      mapAsync (f >> async.Return) input
  
    /// Same as AsyncSeq.iterAsync, but the specified function is synchronous
    /// and performs the side-effect immediately.
    let iter f (input : AsyncSeq<'T>) =
      iterAsync (f >> async.Return) input
  
    /// Same as AsyncSeq.chooseAsync, but the specified function is synchronous
    /// and processes the input element immediately.
    let choose f (input : AsyncSeq<'T>) =
      chooseAsync (f >> async.Return) input
  
    /// Same as AsyncSeq.filterAsync, but the specified predicate is synchronous
    /// and processes the input element immediately.
    let filter f (input : AsyncSeq<'T>) =
      filterAsync (f >> async.Return) input
  
    // --------------------------------------------------------------------------
    // Converting from/to synchronous sequences or IObservables
  
    /// Creates an asynchronous sequence that lazily takes element from an
    /// input synchronous sequence and returns them one-by-one.
    let ofSeq (input : seq<'T>) = asyncSeq {
      for el in input do
        yield el }
  
    /// A helper type for implementation of buffering when converting
    /// observable to an asynchronous sequence
    type internal BufferMessage<'T> =
      | Get of AsyncReplyChannel<'T>
      | Put of 'T
  
  
  
    // --------------------------------------------------------------------------
  
    /// Combines two asynchronous sequences into a sequence of pairs.
    /// The values from sequences are retrieved in parallel.
    let rec zip (input1 : AsyncSeq<'T1>) (input2 : AsyncSeq<'T2>) : AsyncSeq<_> = async {
      let! ft = input1 |> Async.StartChild
      let! s = input2
      let! f = ft
      match f, s with
      | Cons(hf, tf), Cons(hs, ts) ->
          return Cons( (hf, hs), zip tf ts)
      | _ -> return Nil }
  
    /// Returns elements from an asynchronous sequence while the specified
    /// predicate holds. The predicate is evaluated asynchronously.
    let rec takeWhileAsync p (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
      let! v = input
      match v with
      | Cons(h, t) ->
          let! res = p h
          if res then
            return Cons(h, takeWhileAsync p t)
          else return Nil
      | Nil -> return Nil }
  
    /// Skips elements from an asynchronous sequence while the specified
    /// predicate holds and then returns the rest of the sequence. The
    /// predicate is evaluated asynchronously.
    let rec skipWhileAsync p (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
      let! v = input
      match v with
      | Cons(h, t) ->
          let! res = p h
          if res then return! skipWhileAsync p t
          else return v
      | Nil -> return Nil }
  
    /// Returns elements from an asynchronous sequence while the specified
    /// predicate holds. The predicate is evaluated synchronously.
    let rec takeWhile p (input : AsyncSeq<'T>) =
      takeWhileAsync (p >> async.Return) input
  
    /// Skips elements from an asynchronous sequence while the specified
    /// predicate holds and then returns the rest of the sequence. The
    /// predicate is evaluated asynchronously.
    let rec skipWhile p (input : AsyncSeq<'T>) =
      skipWhileAsync (p >> async.Return) input
  
    /// Returns the first N elements of an asynchronous sequence
    let rec take count (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
      if count > 0 then
        let! v = input
        match v with
        | Cons(h, t) ->
            return Cons(h, take (count - 1) t)
        | Nil -> return Nil
      else return Nil }
  
    /// Skips the first N elements of an asynchronous sequence and
    /// then returns the rest of the sequence unmodified.
    let rec skip count (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
      if count > 0 then
        let! v = input
        match v with
        | Cons(h, t) ->
            return! skip (count - 1) t
        | Nil -> return Nil
      else return! input }
  
    /// Creates an async computation which iterates the AsyncSeq and collects the output into an array.
    let toArray (input:AsyncSeq<'T>) : Async<'T[]> =
      input
      |> fold (fun (arr:ResizeArray<_>) a -> arr.Add(a) ; arr) (new ResizeArray<_>())
      |> Async.map (fun arr -> arr.ToArray())
  
    /// Creates an async computation which iterates the AsyncSeq and collects the output into a list.
    let toList (input:AsyncSeq<'T>) : Async<'T list> =
      input
      |> fold (fun arr a -> a::arr) []
      |> Async.map List.rev
  
    /// Generates an async sequence using the specified generator function.
    let rec unfoldAsync (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = asyncSeq {
      let! r = f s
      match r with
      | Some (a,s) ->
        yield a
        yield! unfoldAsync f s
      | None -> () }
  
    /// Flattens an AsyncSeq of sequences.
    let rec concatSeq (input:AsyncSeq<#seq<'T>>) : AsyncSeq<'T> = asyncSeq {
      let! v = input
      match v with
      | Nil -> ()
      | Cons (hd, tl) ->
        for item in hd do
          yield item
        yield! concatSeq tl }
  
    /// Interleaves two async sequences into a resulting sequence. The provided
    /// sequences are consumed in lock-step.
    let interleave =
  
      let rec left (a:AsyncSeq<'a>) (b:AsyncSeq<'b>) : AsyncSeq<Choice<_,_>> = async {
        let! a = a
        match a with
        | Cons (a1, t1) -> return Cons (Choice1Of2 a1, right t1 b)
        | Nil -> return! b |> map Choice2Of2 }
  
      and right (a:AsyncSeq<'a>) (b:AsyncSeq<'b>) : AsyncSeq<Choice<_,_>> = async {
        let! b = b
        match b with
        | Cons (a2, t2) -> return Cons (Choice2Of2 a2, left a t2)
        | Nil -> return! a |> map Choice1Of2 }
  
      left
  
  
    /// Buffer items from the async sequence into buffers of a specified size.
    /// The last buffer returned may be less than the specified buffer size.
    let rec bufferByCount (bufferSize:int) (s:AsyncSeq<'T>) : AsyncSeq<'T[]> =
      if (bufferSize < 1) then invalidArg "bufferSize" "must be positive"
      async {
        let buffer = ResizeArray<_>()
        let rec loop s = async {
          let! step = s
          match step with
          | Nil ->
            if (buffer.Count > 0) then return Cons(buffer.ToArray(),async.Return Nil)
            else return Nil
          | Cons(a,tl) ->
            buffer.Add(a)
            if buffer.Count = bufferSize then
              let buf = buffer.ToArray()
              buffer.Clear()
              return Cons(buf, loop tl)
            else
              return! loop tl
        }
        return! loop s
      }
          
    [<AutoOpen>]
    module AsyncSeqExtensions =
        open System.Text
        
        /// Builds an asynchronou sequence using the computation builder syntax
        let asyncSeq = new AsyncSeqBuilder()
  
        // Add asynchronous for loop to the 'async' computation builder
        type Microsoft.FSharp.Control.AsyncBuilder with
          member x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) =
            async.Bind(seq, function
              | Nil -> async.Zero()
              | Cons(h, t) -> async.Combine(action h, x.For(t, action)))
        
  
        type System.IO.Stream with
            member this.AsyncReadSeq(?bufferSize) : AsyncSeq<byte[]> =
                let bufferSize = defaultArg bufferSize (2 <<< 16)
                let temp : byte[] = Array.zeroCreate bufferSize
                let rec doRead () = async {
                    let! count = this.AsyncRead(temp, 0, bufferSize)
                    if count = 0 then return Nil
                    else
                        let buf = Array.sub temp 0 count
                        return Cons(buf, doRead ())
                    }
                doRead ()
  
            member this.AsyncWriteSeq(seq : AsyncSeq<byte[]>) =
                let rec run s = async {
                    let! chunk = s
                    match chunk with
                    | Nil -> return ()
                    | Cons(data, next) ->
                        do! this.AsyncWrite(data)
                        return! run next
                    }
                run seq
