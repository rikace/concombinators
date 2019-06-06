module FunConcurrency.Asynchronous.AsyncModule

open System
open System.Net
open System.IO
open System.Threading.Tasks
open Combinators.AsyncEx.AsyncResult
open Combinators.AsyncEx
open Combinators.AsyncEx.AsyncCombinators

type T = { Url : string }

let xs = [
    { Url = "http://microsoft.com" }
    { Url = "thisDoesNotExists" } // throws when constructing Uri, before downloading
    { Url = "https://thisDotNotExist.Either" }
    { Url = "http://google.com" }
]

let isAllowedInFileName c =
    not <| Seq.contains c (Path.GetInvalidFileNameChars())

let downloadAsync url =
    async {
        use client = new WebClient()
        printfn "Downloading %s ..." url
        let! data = client.DownloadStringTaskAsync(Uri(url)) |> Async.AwaitTask
        return (url, data)
    }

let saveAsync (url : string, data : string) =
    let destination = url // fix here to change the file name generation as needed
    async {
        let fn =
            [|
                __SOURCE_DIRECTORY__
                destination |> Seq.filter isAllowedInFileName |> String.Concat
            |]
            |> Path.Combine
        printfn "saving %s ..." (Path.GetFileName destination)
        use stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 0x100)
        use writer = new StreamWriter(stream)
        do! writer.WriteAsync(data) |> Async.AwaitTask
        return (url, data)
        }

xs
|> Seq.map (fun u -> downloadAsync u.Url)
|> Async.Parallel
|> Async.RunSynchronously
|> Seq.iter(fun (u: string,d: string) -> printfn "Downloaded %s - size %d" u d.Length)


open Combinators.AsyncEx.AsyncResult.AsyncHandler

xs
|> List.map(fun u -> AsyncResult.wrap (downloadAsync u.Url))
|> List.map(fun x -> AsyncHandler.ofAsyncResult x)
|> Async.Parallel
|> Async.RunSynchronously
|> Seq.iter (function
    | Ok data -> printfn "Succeeded"
    | Error exn -> printfn "Failed with %s" exn.Message)


// type AsyncResult<'a> = Async<Result<'a, exn>>



let downloadAsync' url =
    AsyncResult.wrap (async {
        use client = new WebClient()
        printfn "Downloading %s ..." url
        let! data = client.DownloadStringTaskAsync(Uri(url)) |> Async.AwaitTask
        return (url, data)
    })

let saveAsync' (url : string, data : string) =
    let destination = url // fix here to change the file name generation as needed
    AsyncResult.wrap (async {
        let fn =
            [|
                __SOURCE_DIRECTORY__
                destination |> Seq.filter isAllowedInFileName |> String.Concat
            |]
            |> Path.Combine
        printfn "saving %s ..." (Path.GetFileName destination)
        use stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 0x100)
        use writer = new StreamWriter(stream)
        do! writer.WriteAsync(data) |> Async.AwaitTask
        return (url, data)
        })


let bind f g = f >> (AsyncResult.flatMap g)
let downloadAndSave = bind downloadAsync' saveAsync'


let (>>=) f g = AsyncResult.flatMap g f

let kleisli (f:'a -> AsyncResult<'b>) (g:'b -> AsyncResult<'c>) (x:'a) = (f x) >>= g

let (>=>) (operation1:'a -> AsyncResult<'b>) (operation2:'b -> AsyncResult<'c>) (value:'a) = operation1 value >>= operation2

let downloadAndSave' = downloadAsync' >=> saveAsync'

xs
|> List.map(fun u -> downloadAndSave u.Url)
|> List.map(fun x -> AsyncHandler.ofAsyncResult x)
|> Async.Parallel
|> Async.RunSynchronously
|> Seq.iter (function
    | Ok data -> printfn "Succeeded"
    | Error exn -> printfn "Failed with %s" exn.Message)
