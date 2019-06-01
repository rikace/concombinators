module FunConcurrency.Asynchronous.AsyncModule

#load "../Combinators/bin/Debug/netcoreapp2.2/"

open System
open System.Net
open System.IO
open System.Threading.Tasks
open FunConcurrency.AsyncCombinators
open FunConcurrency

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



// Step (1)
// How can you handle the exception in case of not existing URL?
// TIP:    avoid the try-catch block and check if there is an existing
//         Async api that could help you
//         In addition, you should still be able to branch logic in both the
//         "success" and "failure" paths using the F# Result<_,_> type



xs
|> List.map(fun u -> AsyncRes.wrap (downloadAsync u.Url))
|> Async.Parallel
|> Async.RunSynchronously
|> Seq.iter (function
    | Ok data -> printfn "Succeeded"
    | Error exn -> printfn "Failed with %s" exn.Message)


// Step (2)
// How can you compose the "saveAsync" function at the end o the pipeline ?
// Example: downloadAsync >> hanlde errors >> saveFunction
// TIP:  first attempt, a "map" function that run async could be useful

// TIP : define and use a new type that combines the Async and Result types,
//       this type definition will be very useful in composition of the voming function


type AsyncRes<'a> = Async<Result<'a, exn>>

xs
|> List.map(fun u -> AsyncRes.wrap (downloadAsync u.Url) |> AsyncRes.map saveAsync)
|> Async.Parallel
|> Async.RunSynchronously
|> Seq.iter (function
    | Ok data -> printfn "Succeeded"
    | Error exn -> printfn "Failed with %s" exn.Message)



// Step (3)
// How can you compose the "downloadAsync" and "saveAsync" functions
// in a more idiomatic way?
// Example: downloadAsync >> saveFunction
// TIP: start wrapping (or lifting) both the "downloadAsync" and "saveAsync" function
//      try to implement the map / bind/ flatMap function for the previously define type

let downloadAsync' url =
    AsyncRes.wrap (async {
        use client = new WebClient()
        printfn "Downloading %s ..." url
        let! data = client.DownloadStringTaskAsync(Uri(url)) |> Async.AwaitTask
        return (url, data)
    })

let saveAsync' (url : string, data : string) =
    let destination = url // fix here to change the file name generation as needed
    AsyncRes.wrap (async {
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


let bind f g = f >> (AsyncRes.flatMap g)
let downloadAndSave = AsyncRes.bind downloadAsync' saveAsync'


// BONUS :
// try to use the "kleisli" combinators, and then create the "fish" infix operator >=> to replace the "kleisli" function
// kleisli signature: (f:'a -> AsyncRes<'b>) (g:'b -> AsyncRes<'c>) (x:'a)

let (>>=) f g = AsyncRes.flatMap g f
let kleisli (f:'a -> AsyncRes<'b>) (g:'b -> AsyncRes<'c>) (x:'a) = (f x) >>= g
let (>=>) (operation1:'a -> AsyncRes<'b>) (operation2:'b -> AsyncRes<'c>) (value:'a) =
                                                                    operation1 value >>= operation2

let downloadAndSave' = downloadAsync' >=> saveAsync'

xs
|> List.map(fun u -> downloadAndSave u.Url)
|> Async.Parallel
|> Async.RunSynchronously
|> Seq.iter (function
    | Ok data -> printfn "Succeeded"
    | Error exn -> printfn "Failed with %s" exn.Message)


// Step (4)
// now that we have implemented the Bind function, let's implement a computation expression
// https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions
//
// complete the Bind functions respecting the function siganuture.
// in this case, it is important to define two Bind functions, as indicated below
// NOTE: you should be able to use any implementaion of the "download" & "save" function
//       (the wrapped and no wrapped version)

type AsyncResComputationExpression() =

    member this.Bind(x: AsyncRes<'a>, f: 'a -> AsyncRes<'b>) : AsyncRes<'b> = async {
        return! AsyncRes.flatMap f x   }

    member this.Bind (m:Async<'a>, f:'a -> AsyncRes<'b>) : AsyncRes<'b> =
        AsyncRes.flatMap f (AsyncRes.wrap m)

    member this.Bind(result : Result<'a, exn>, binder : 'a -> AsyncRes<'b>) : AsyncRes<'b> =
        this.Bind(result |> async.Return, binder)

    member this.Delay(f) = f()
    member this.Return m = AsyncRes.retn m
    member this.ReturnFrom (m : AsyncRes<'a>) = m
    member this.Zero() : AsyncRes<unit> = this.Return()

// Step (5)
// uncompent the following code to verify that the CE works corretcly
let asyncRes = AsyncResComputationExpression()

let comp url = asyncRes {
    let! resDownload = downloadAsync url
    let! resSaving = saveAsync resDownload
    return resSaving
    }

xs
|> List.map(fun u -> comp u.Url)
|> Async.Parallel
|> Async.RunSynchronously
|> Seq.iter (function
    | Ok data -> printfn "Succeeded"
    | Error exn -> printfn "Failed with %s" exn.Message)
