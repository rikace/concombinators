namespace FSharp.Parallelx

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading

[<AutoOpen>]
module Utilities =
    
    let synchronize f = 
      let ctx = System.Threading.SynchronizationContext.Current 
      f (fun g arg ->
          let nctx = System.Threading.SynchronizationContext.Current 
          if ctx <> null && ctx <> nctx then ctx.Post((fun _ -> g(arg)), null)
          else g(arg) )    
    
    let charDelimiters = [0..256] |> Seq.map(char)|> Seq.filter(fun c -> Char.IsWhiteSpace(c) || Char.IsPunctuation(c)) |> Seq.toArray

    /// Transforms a function by flipping the order of its arguments.
    let inline flip f a b = f b a

    let inline diag a = a,a
    
    /// Given a value, apply a function to it, ignore the result, then return the original value.
    let inline tap fn x = fn x |> ignore; x

    /// Sequencing operator like Haskell's ($). Has better precedence than (<|) due to the
    /// first character used in the symbol.
    let (^) = (<|)

    /// Given a value, apply a function to it, ignore the result, then return the original value.
    let inline tee fn x = fn x |> ignore; x

    /// Custom operator for `tee`: Given a value, apply a function to it, ignore the result, then return the original value.
    let inline (|>!) x fn = tee fn x

    let force (x: Lazy<'T>) = x.Force()

    /// Safely invokes `.Dispose()` on instances of `IDisposable`
    let inline dispose (d :#IDisposable) = match box d with null -> () | _ -> d.Dispose()

    let is<'T> (x: obj) = x :? 'T

    let delimiters =
            [0..256]
            |> List.map(char)
            |> List.filter(fun c -> Char.IsWhiteSpace(c) || Char.IsPunctuation(c))
            |> List.toArray
            
    let con<'a> (o:obj) =
        match Convert.ChangeType(o, typeof<'a>) with
        | :? 'a as k -> Some k
        | _ -> None

    let conty (ty:Type) (o:obj) : 'a option=
        match Convert.ChangeType(o, ty) with
        | :? 'a as k when typeof<'a> = ty -> Some k
        | _ -> None

    
    [<Sealed>]
    type private ReferenceEqualityComparer<'T when 'T : not struct and 'T : equality>() =
        interface IEqualityComparer<'T> with
            member __.Equals(x, y) = obj.ReferenceEquals(x, y)
            member __.GetHashCode(x) = x.GetHashCode()
    
    [<Sealed>]
    type private EquatableEqualityComparer<'T when 'T :> IEquatable<'T> and 'T : struct and 'T : equality>() =
        interface IEqualityComparer<'T> with
            member __.Equals(x, y) = x.Equals(y)
            member __.GetHashCode(x) = x.GetHashCode()
    
    [<Sealed>]
    type private AnyEqualityComparer<'T when 'T : equality>() =
        interface IEqualityComparer<'T> with
            member __.Equals(x, y) = x.Equals(y)
            member __.GetHashCode(x) = x.GetHashCode()
 
    let inline (|??) (a: 'a Nullable) (b: 'a) = if a.HasValue then a.Value else b
    
    let private consoleColor (color : ConsoleColor) =
        let current = Console.ForegroundColor
        Console.ForegroundColor <- color
        { new IDisposable with
          member x.Dispose() = Console.ForegroundColor <- current }

    let cprintf color str =
        Printf.kprintf (fun s -> use c = consoleColor color in printf "%s" s) str
    
    module Log =
    
        let report =
            let lockObj = obj()
            fun (color : ConsoleColor) (message : string) ->
                lock lockObj (fun _ ->
                    Console.ForegroundColor <- color
                    printfn "%s (thread ID: %i)" 
                        message Thread.CurrentThread.ManagedThreadId
                    Console.ResetColor())
    
        let red = report ConsoleColor.Red
        let green = report ConsoleColor.Green
        let yellow = report ConsoleColor.Yellow
        let cyan = report ConsoleColor.Cyan
    
    
    type String with    
        member x.IsEmpty
            with get() = String.IsNullOrEmpty(x) || String.IsNullOrWhiteSpace(x)
            
    module GC =
        let clean () =
            for i=1 to 2 do
                GC.Collect ()
                GC.WaitForPendingFinalizers ()
            Thread.Sleep 10
            

    let rec foldk f (acc:'State) xs =
        match xs with
        | []    -> acc
        | x::xs -> f acc x (fun lacc -> foldk f lacc xs)
        
        
    module StreamHelpers =
        open System.IO
        open System.IO.Compression
        open FSharp.Parallelx
        
        type System.IO.Stream with
            member x.WriteBytesAsync (bytes : byte []) =
                task {
                    do! x.WriteAsync(BitConverter.GetBytes bytes.Length, 0, sizeof<int>)
                    do! x.WriteAsync(bytes, 0, bytes.Length)
                    do! x.FlushAsync()
                }

            member x.ReadBytesAsync(length : int) =
                let rec readSegment buf offset remaining =
                    task {
                        let! read = x.ReadAsync(buf, offset, remaining)
                        if read < remaining then
                            return! readSegment buf (offset + read) (remaining - read)
                        else
                            return ()
                    }

                task {
                    let bytes = Array.zeroCreate<byte> length
                    do! readSegment bytes 0 length
                    return bytes
                }

            member x.ReadBytesAsync() =
                task {
                    let! lengthArr = x.ReadBytesAsync sizeof<int>
                    let length = BitConverter.ToInt32(lengthArr, 0)
                    return! x.ReadBytesAsync length
                }
                
        let compress (f:MemoryStream -> Stream) (data:byte[]) =
            use targetStream = new MemoryStream()
            use sourceStream = new MemoryStream(data)
            use zipStream = f targetStream
            sourceStream.CopyTo(zipStream)
            targetStream.ToArray()
            
        let decompress (f:MemoryStream -> Stream) (data:byte[]) =
            use targetStream = new MemoryStream()
            use sourceStream = new MemoryStream(data)
            use zipStream = f sourceStream
            zipStream.CopyTo(targetStream)
            targetStream.ToArray()            
                        
        let compressGZip data =
            compress (fun memStream -> new GZipStream(memStream, CompressionMode.Compress) :> Stream) data

        let compressDeflate data =
            compress (fun memStream -> new DeflateStream(memStream, CompressionMode.Compress) :> Stream) data

        let decompressGZip data =
            decompress (fun memStream -> new GZipStream(memStream, CompressionMode.Decompress) :> Stream) data

        let decompressDeflate data =
            decompress (fun memStream -> new DeflateStream(memStream, CompressionMode.Decompress) :> Stream) data
            
module Benchmark =
    /// Do countN repetitions of the function f and print the
    /// time elapsed, number of GCs and change in total memory
    let time countN label f  =

        let stopwatch = System.Diagnostics.Stopwatch()

        // do a full GC at the start but NOT thereafter
        // allow garbage to collect for each iteration
        System.GC.Collect()
        printfn "Started"

        let getGcStats() =
            let gen0 = System.GC.CollectionCount(0)
            let gen1 = System.GC.CollectionCount(1)
            let gen2 = System.GC.CollectionCount(2)
            let mem = System.GC.GetTotalMemory(false)
            gen0,gen1,gen2,mem


        printfn "======================="
        printfn "%s" label
        printfn "======================="
        for iteration in [1..countN] do
            let gen0,gen1,gen2,mem = getGcStats()
            stopwatch.Restart()
            f()
            stopwatch.Stop()
            let gen0',gen1',gen2',mem' = getGcStats()
            // convert memory used to K
            let changeInMem = (mem'-mem) / 1000L
            printfn "#%2i elapsed:%6ims gen0:%3i gen1:%3i gen2:%3i mem:%6iK" iteration stopwatch.ElapsedMilliseconds (gen0'-gen0) (gen1'-gen1) (gen2'-gen2) changeInMem

module Dynamic =
    open System.Reflection

    let (?) (thingey : obj) (propName: string) : 'a =
        let propInfo = thingey.GetType().GetProperty(propName)
        propInfo.GetValue(thingey, null) :?> 'a

    let (?<-) (thingey : obj) (propName : string) (newValue : 'a) =
        let propInfo = thingey.GetType().GetProperty(propName)
        propInfo.SetValue(thingey, newValue, null)
        
        
module CPUOperationSimulation =
    
    open System
    open System.Drawing
    open System.Diagnostics.CodeAnalysis
    open System.Collections.Generic
    open System.Diagnostics
    open System.Globalization
    open System.IO
    open System.Linq
    open System.Threading    

    /// <summary>
    /// Simulates a CPU-intensive operation on a single core. The operation will use approximately 100% of a
    /// single CPU for a specified duration.
    /// </summary>
    /// <param name="seconds">The approximate duration of the operation in seconds</param>
    /// <param name="token">A token that may signal a request to cancel the operation.</param>
    /// <param name="throwOnCancel">true if an execption should be thrown in response to a cancellation request.</param>
    /// <returns>true if operation completed normally false if the user canceled the operation</returns>
    let DoCpuIntensiveOperation seconds (token:CancellationToken) throwOnCancel =
        if (token.IsCancellationRequested) then
            if (throwOnCancel) then token.ThrowIfCancellationRequested()
            false
        else
            let ms = int64 (seconds * 1000.0)
            let sw = new Stopwatch()
            sw.Start()
            let checkInterval = Math.Min(20000000, int (20000000.0 * seconds))

            // Loop to simulate a computationally intensive operation
            let rec loop i = 
                // Periodically check to see if the user has requested 
                // cancellation or if the time limit has passed
                let check = seconds = 0.0 || i % checkInterval = 0
                if check && token.IsCancellationRequested then
                    if throwOnCancel then token.ThrowIfCancellationRequested()
                    false
                elif check && sw.ElapsedMilliseconds > ms then
                    true
                else 
                  loop (i + 1)
          
            // Start the loop with 0 as the first value
            loop 0

    /// <summary>
    /// Simulates a CPU-intensive operation on a single core. The operation will use approximately 100% of a
    /// single CPU for a specified duration.
    /// </summary>
    /// <param name="seconds">The approximate duration of the operation in seconds</param>
    /// <returns>true if operation completed normally false if the user canceled the operation</returns>
    let DoCpuIntensiveOperationSimple seconds =
        DoCpuIntensiveOperation seconds CancellationToken.None false


    // vary to simulate I/O jitter
    let SleepTimeouts = 
      [| 65; 165; 110; 110; 185; 160; 40; 125; 275; 110; 80; 190; 70; 165; 
         80; 50; 45; 155; 100; 215; 85; 115; 180; 195; 135; 265; 120; 60; 
         130; 115; 200; 105; 310; 100; 100; 135; 140; 235; 205; 10; 95; 175; 
         170; 90; 145; 230; 365; 340; 160; 190; 95; 125; 240; 145; 75; 105; 
         155; 125; 70; 325; 300; 175; 155; 185; 255; 210; 130; 120; 55; 225;
         120; 65; 400; 290; 205; 90; 250; 245; 145; 85; 140; 195; 215; 220;
         130; 60; 140; 150; 90; 35; 230; 180; 200; 165; 170; 75; 280; 150; 
         260; 105 |]


    /// <summary>
    /// Simulates an I/O-intensive operation on a single core. The operation will use only a small percent of a
    /// single CPU's cycles however, it will block for the specified number of seconds.
    /// </summary>
    /// <param name="seconds">The approximate duration of the operation in seconds</param>
    /// <param name="token">A token that may signal a request to cancel the operation.</param>
    /// <param name="throwOnCancel">true if an execption should be thrown in response to a cancellation request.</param>
    /// <returns>true if operation completed normally false if the user canceled the operation</returns>
    let DoIoIntensiveOperation seconds (token:CancellationToken) throwOnCancel =
        if token.IsCancellationRequested then false else
        let ms = int (seconds * 1000.0)
        let sw = new Stopwatch()
        let timeoutCount = SleepTimeouts.Length
        sw.Start()

        // Loop to simulate I/O intensive operation
        let mutable i = Math.Abs(sw.GetHashCode()) % timeoutCount
        let mutable result = None
        while result = None do
            let timeout = SleepTimeouts.[i]
            i <- (i + 1) % timeoutCount

            // Simulate I/O latency
            Thread.Sleep(timeout)

            // Has the user requested cancellation? 
            if token.IsCancellationRequested then
                if throwOnCancel then token.ThrowIfCancellationRequested()
                result <- Some false

            // Is the computation finished?
            if sw.ElapsedMilliseconds > int64 ms then
                result <- Some true
      
        result.Value


    /// <summary>
    /// Simulates an I/O-intensive operation on a single core. The operation will use only a small percent of a
    /// single CPU's cycles however, it will block for the specified number of seconds.
    /// </summary>
    /// <param name="seconds">The approximate duration of the operation in seconds</param>
    /// <returns>true if operation completed normally false if the user canceled the operation</returns>
    let DoIoIntensiveOperationSimple seconds =
        DoIoIntensiveOperation seconds CancellationToken.None false


    /// Simulates an I/O-intensive operation on a single core. The operation will 
    /// use only a small percent of a single CPU's cycles however, it will block 
    /// for the specified number of seconds.
    ///
    /// This is same as 'DoIoIntensiveOperation', but uses F# asyncs to simulate
    /// non-blocking (asynchronous) I/O typical in F# async applications.
    let AsyncDoIoIntensiveOperation seconds (token:CancellationToken) throwOnCancel = 
      async { if token.IsCancellationRequested then return false else
              let ms = int (seconds * 1000.0)
              let sw = new Stopwatch()
              let timeoutCount = SleepTimeouts.Length
              sw.Start()

              // Loop to simulate I/O intensive operation
              let i = ref (Math.Abs(sw.GetHashCode()) % timeoutCount)
              let result = ref None
              while !result = None do
                  let timeout = SleepTimeouts.[!i]
                  i := (!i + 1) % timeoutCount

                  // Simulate I/O latency
                  do! Async.Sleep(timeout)

                  // Has the user requested cancellation? 
                  if token.IsCancellationRequested then
                      if throwOnCancel then token.ThrowIfCancellationRequested()
                      result := Some false

                  // Is the computation finished?
                  if sw.ElapsedMilliseconds > int64 ms then
                      result := Some true
      
              return result.Value.Value }


    /// Simulates an I/O-intensive operation on a single core. The operation will 
    /// use only a small percent of a single CPU's cycles however, it will block 
    /// for the specified number of seconds.
    ///
    /// This is same as 'DoIoIntensiveOperationSimple', but uses F# asyncs to simulate
    /// non-blocking (asynchronous) I/O typical in F# async applications.
    let AsyncDoIoIntensiveOperationSimple seconds = 
        AsyncDoIoIntensiveOperation seconds CancellationToken.None false    
    