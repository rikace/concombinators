module Helpers.FSharp.RequestGate

type RequestGate(n:int) =
    let semaphore = new System.Threading.Semaphore(n,n)
    member x.Aquire(?timeout) =
        async { let! ok = Async.AwaitWaitHandle(semaphore, ?millisecondsTimeout=timeout)
                if ok then return { new System.IDisposable with
                                        member x.Dispose() =
                                            semaphore.Release() |> ignore }
                else return! failwith "Handle not aquired" }


open System.IO
open System.Net
open System

let sites =
    [   "http://www.live.com";      "http://www.fsharp.org";
        "http://news.live.com";     "http://www.digg.com";
        "http://www.yahoo.com";     "http://www.amazon.com"
        "http://news.yahoo.com";    "http://www.microsoft.com";
        "http://www.google.com";    "http://www.netflix.com";
        "http://news.google.com";   "http://www.maps.google.com";
        "http://www.bing.com";      "http://www.microsoft.com";
        "http://www.facebook.com";  "http://www.docs.google.com";
        "http://www.youtube.com";   "http://www.gmail.com";
        "http://www.reddit.com";    "http://www.twitter.com";   ]

let httpAsync (url : string) = async {
    let req = WebRequest.Create(url)
    let! resp = req.AsyncGetResponse()
    use stream = resp.GetResponseStream()
    use reader = new StreamReader(stream)
    let! text = reader.ReadToEndAsync() |> Async.AwaitTask
    return text
}

let runAsync () =
    sites
    |> Seq.map httpAsync
    |> Async.Parallel
    |> Async.RunSynchronously

let gate = RequestGate(2)

let httpAsyncThrottle (url : string) = async {
    let req = WebRequest.Create(url)
    use! g = gate.Aquire()
    let! resp = req.AsyncGetResponse()
    use stream = resp.GetResponseStream()
    use reader = new StreamReader(stream)
    let! text = reader.ReadToEndAsync() |> Async.AwaitTask
    return text
}

let runAsyncThrottle () =
    sites
    |> Seq.map httpAsyncThrottle
    |> Async.Parallel
    |> Async.RunSynchronously


runAsync ()
runAsyncThrottle ()


// ===========================================
// Async Cancellation Handling
// ===========================================

let getCancellationToken() = new System.Threading.CancellationTokenSource()

let htmlOfSites =
    sites
    |> Seq.map httpAsyncThrottle
    |> Async.Parallel

let cancellationToken = getCancellationToken()
Async.Start(htmlOfSites |> Async.Ignore, cancellationToken=cancellationToken.Token)

cancellationToken.Cancel()



let cancellationToken' = getCancellationToken()

// Callback used when the operation is canceled
let cancelHandler (ex : OperationCanceledException) =
    printfn "The task has been canceled."


let tryCancelledAsyncOp = Async.TryCancelled(htmlOfSites |> Async.Ignore, cancelHandler)

Async.Start(tryCancelledAsyncOp, cancellationToken=cancellationToken'.Token)

cancellationToken'.Cancel()