namespace ConCombinators

//module Channel =
//    
//    type ICannel =
//        abstract create : String (*name*) -> type -> Channel
//        abstract give : Channel -> 'a -> unit
//        abstract giveAsync : Channel -> 'a -> Async<unit>
//        abstract take : Channel -> 'a
//        abstract takeAsync : Channel -> Async<'a> // send is async ??
//        
//
//let orElse a b = a ||b        
//        
//let Either (p1: Parser<'a>) (p2: Parser<'a>) : Parser<'a> =
//    let p stream =
//        match p1 stream with
//        | Failure -> p2 stream
//        | res -> res
//    in p
//
//// This is the Either combinator defined in the previous blog post.
//let (<|>) = Either
//
///// Applies parsers p1 and p2 returning the result of p2.
//let (>>.) p1 p2 : Parser<'b> =
//    p1 >>= (fun _ -> p2)
//
///// Applies parsers p1 and p2 returning the result of p1.
//let (.>>) p1 p2 : Parser<'a> =
//    p1 >>= (fun x -> p2 >>% x)
//    
///// Applies parsers p1 and p2 returning both results.
//let (.>>.) p1 p2: Parser<'a*'b> =
//    p1 >>= (fun x -> p2 >>= (fun y -> Return (x, y)))
    
    
module Test =
  
  type [<Struct>] myStruct(x : int) =
    member __.X = x
    
  
  let (<*>) f l = f |> List.collect (fun g -> l |> List.map g)
  
  
  let rec foldk f (acc:'State) xs =
      match xs with
      | []    -> acc
      | x::xs -> f acc x (fun lacc -> foldk f lacc xs)
      
  let synchronize f = 
      let ctx = System.Threading.SynchronizationContext.Current 
      f (fun g arg ->
          let nctx = System.Threading.SynchronizationContext.Current 
          if ctx <> null && ctx <> nctx then ctx.Post((fun _ -> g(arg)), null)
          else g(arg) )    
    
module CallCC =
    // An implementation of call-with-current-continuation for Async.
  
    // this operation capturs the current continuation and passes it into the current expressin
    
    // callccK    ((a -> K b) -> K a) -> Ka
    // callccK h  = fun c -> let k a = fun d -> c a in h k c
    
    // the argument to callccK is a function h, which is passed a function k of type (a -> K b).
    // if k is called with argumnet a, it ignires its continuation d and passes a to the captured
    //    continuation c instead. 
    
  let callcc (f: ('a -> Async<'b>) -> Async<'a>) : Async<'a> =
    Async.FromContinuations(fun (cont, econt, ccont) ->
      Async.StartWithContinuations(f (fun a -> Async.FromContinuations(fun (_, _, _) -> cont a)), cont, econt, ccont))

  (* Test callcc *)
  let sum l =
    let rec sum l = async {
      let! result = callcc (fun exit1 -> async {
        match l with
        | [] -> return 0
        | h::t when h = 2 -> return! exit1 42
        | h::t -> let! r = sum t
                  return h + r })
      return result }
    Async.RunSynchronously(sum l)

  let ``When summing a list without a 2 via callcc it should return 8``() =
    sum [1;1;3;3] = 8

  let ``When summing a list containing 2 via callcc it should return 43``() =
    sum [1;2;3] = 43
    
    
module AsyncChoice =
  
  open System.Threading

  // Async.Choice method that takes several workflows and creates a workflow, which returns the first result that was computed.
  // After a workflow completes, remaining workflows are cancelled using the F# async cancellation mechanism.
  type Microsoft.FSharp.Control.Async with
    /// Takes several asynchronous workflows and returns 
    /// the result of the first workflow that successfuly completes
    static member Choice(workflows) = 
      Async.FromContinuations(fun (cont, _, _) ->
        let cts = new CancellationTokenSource()
        let completed = ref false
        let lockObj = new obj()
        let synchronized f = lock lockObj f
      
        /// Called when a result is available - the function uses locks
        /// to make sure that it calls the continuation only once
        let completeOnce res =
          let run =
            synchronized(fun () ->
              if completed.Value then false
              else completed := true; true)
          if run then cont res
      
        /// Workflow that will be started for each argument - run the 
        /// operation, cancel pending workflows and then return result
        let runWorkflow workflow = async {
          let! res = workflow
          cts.Cancel()
          completeOnce res }
      
        // Start all workflows using cancellation token
        for work in workflows do
          Async.Start(runWorkflow work, cts.Token) )
  
  /// Simple function that sleeps for some time 
  /// and then returns a specified value
  let delayReturn n s = async {
    // Run a couple of async operations - F# checks for 
    // cancellation automatically when starting them
    do! Async.Sleep(n) 
    do! Async.Sleep(1)  
    printfn "returning %s" s
    return s }

  // Run two 'delayReturn' workflows in parallel and return
  // the result of the first one. Note that this only prints
  // 'returning First!' (because the other workflow is cancelled)
  Async.Choice [ delayReturn 1000 "First!"; delayReturn 5000 "Second!" ]
  |> Async.RunSynchronously
  
  
module AsyncDemo =
  open System
  open System.IO
  open System.Net
  open System.Threading

  // ------------------------------------------------------------------
  // Read a stream into the memory and then return it as a string
  // ------------------------------------------------------------------

  let readToEnd (stream:Stream) = async {
    // Allocate 1kb buffer for downloading dat
    let buffer = Array.zeroCreate 1024
    use output = new MemoryStream()
    let finished = ref false
    
    while not finished.Value do
      // Download one (at most) 1kb chunk and copy it
      let! count = stream.AsyncRead(buffer, 0, 1024)
      do! output.AsyncWrite(buffer, 0, count)
      finished := count <= 0

    // Read all data into a string
    output.Seek(0L, SeekOrigin.Begin) |> ignore
    use sr = new StreamReader(output)
    return sr.ReadToEnd() }


  // ------------------------------------------------------------------
  // Download a web page using HttpWebRequest
  // ------------------------------------------------------------------

  /// Downlaod content of a web site using 'readToEnd'
  let download url = async {
    let request = HttpWebRequest.Create(Uri(url))
    use! response = request.AsyncGetResponse()
    use stream = response.GetResponseStream()
    let! res = readToEnd stream 
    return res }

  // Create a list of sites to download 

  let sites = 
    [ "http://webstep.no"
      "http://microsoft.com"
      "http://bing.com"
      "http://google.com" ]

  // Fork-join parallelism (download all)
  let downloads = sites |> List.map download
  let alldata = Async.Parallel downloads

  // Run synchronously & block the thread
  let res = Async.RunSynchronously(alldata)

  // Create a background task that will eventually
  // print (or do some other side-effect)
  let work = async { 
    let! res = alldata 
    for it in res do
      printfn "%d" it.Length }

  Async.Start(work)
    
  
module AsyncAndReactice =
  open System

  // Implements a simple Async.StartDisposable extension that can be used to easily create IObservable values from F# asynchronous workflows.
  // The method starts an asynchronous workflow and returns IDisposable that cancels the workflow when disposed.
  type Microsoft.FSharp.Control.Async with
    /// Starts the specified operation using a new CancellationToken and returns
    /// IDisposable object that cancels the computation. This method can be used
    /// when implementing the Subscribe method of IObservable interface.
    static member StartDisposable(op:Async<unit>) =
      let ct = new System.Threading.CancellationTokenSource()
      Async.Start(op, ct.Token)
      { new IDisposable with 
          member x.Dispose() = ct.Cancel() }
  
  /// Creates IObservable that fires numbers with specified delay
  let createCounterEvent (delay) =

    /// Recursive async computation that triggers observer
    let rec counter (observer:IObserver<_>) count = async {
      do! Async.Sleep(delay)
      observer.OnNext(count)
      return! counter observer (count + 1) }

    // Return new IObservable that starts 'counter' using
    // 'StartDisposable' when a new client subscribes.
    { new IObservable<_> with
        member x.Subscribe(observer) =
          counter observer 0
          |> Async.StartDisposable }

  // Start the counter with 1 second delay and print numbers
  let disp = 
    createCounterEvent 1000
    |> Observable.map (sprintf "Count: %d")
    |> Observable.subscribe (printfn "%s")

  // Dispose of the observer (and underlying async)
  disp.Dispose()
  
    
  
module AsynchronousSequence = 
  open System.IO

  // An asynchronous sequence is similar to the seq type, but the elements of the sequence
  // are generated asynchronously without blocking the caller as in Async.
  // This snippet declares asynchronous sequence and uses it to compare two files in 1k block
  
  /// Represents a sequence of values 'T where items 
  /// are generated asynchronously on-demand
  type AsyncSeq<'T> = Async<AsyncSeqInner<'T>> 
  and AsyncSeqInner<'T> =
    | Ended
    | Item of 'T * AsyncSeq<'T>
  
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
  
  
  
module Queen =
  /// Generate all X,Y coordinates on the board
  /// (initially, all of them are available)
  let all = 
    [ for x in 0 .. 7 do
      for y in 0 .. 7 do
      yield x, y ]

  /// Given available positions on the board, filter
  /// out those that are taken by a newly added queen 
  /// at the position qx, qy
  let filterAvailable (qx, qy) available = 
    available
    |> List.filter (fun (x, y) ->
      // horizontal & vertical
      x <> qx && y <> qy && 
      // two diagonals
      (x-y) <> (qx-qy) &&
      (7-x-y) <> (7-qx-qy))

  /// Generate all solutions. Given already added queens
  /// and remaining available positions, we handle 3 cases:
  ///  1. we have 8 queens - yield their locations
  ///  2. we have no available places - nothing :(
  ///  3. we have available place 
  let rec solve queens available = seq {
    match queens, available with 
    | q, _ when List.length q = 8 -> yield queens
    | _, [] -> ()
    | _, a::available ->
        // generate all solutions with queen at 'a'
        yield! solve (a::queens) (filterAvailable a available)
        // generate all solutions with nothing at 'a'
        yield! solve queens available }

  /// Nicely render the queen locations
  let render items = 
    let arr = Array.init 8 (fun _ -> Array.create 8 " . ") 
    for x, y in items do arr.[x].[y] <- " x "
    for a in arr do printfn "%s" (String.concat "" a)
    printfn "%s" (String.replicate 24 "-")

  // Print all solutions :-)
  solve [] all |> Seq.iter render