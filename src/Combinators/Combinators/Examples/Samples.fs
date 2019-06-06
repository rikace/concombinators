module Examples
open System.Collections
open System.Threading
open System.Linq
open System.Collections.Generic
open System.Collections.Concurrent
open System.Threading.Tasks
open Combinators.Atom
open FSharp.Parallelx.ContextInsensitive
open System.Collections.Immutable

module String = 
    let mempty = ""
    let mappend x y = sprintf "%s%s" x y

    //our aggregate function in fsharp without implicit first element as initial seed.
    //You can provide traverse function from list to string like this : 
    let ofList = List.fold mappend mempty //We are using partial application on the list


// the list does not provide a function with implicit zero as head, 
// you have to provide the neutral by yourself. The monoid is implicit but necessary when you fold/traverse structure    
String.mappend "hello" " world" = (String.ofList ["hello"; " "; "world"])






module Kleisli =
    let upper x = (x:string).ToUpperInvariant()
    let words s = (s:string).Split(' ')

    //If we want to explain what we are doing with a log, we can use pair to get the log : 

    type Writer<'a> = Writer of 'a * string

    //Explained functions
    let toUpper x = Writer (upper x, "toUpper ")
    let toWords x = Writer (words x, "toWords ")
    let identity x = Writer (x, "")

    //Composition
    let process' x = 
        let (Writer (y, l1)) = toUpper x
        let (Writer (z, l2)) = toWords y
        Writer (z, l1 + l2)

    process' "hello world" 

    //How to compose more than 2 explained functions ?

    //Here is the Kleili composition. It is like the process' function excepts that function are supplied as parameter.
    module Writer = 
        module Operators = 
            let (>=>) f g = 
                fun x -> 
                    let (Writer (y, l1)) = f x
                    let (Writer (z, l2)) = g y
                    Writer (z, l1 + l2)

    open Writer.Operators

    //Here is our final function composition. We can add other function easily with the fish operator
    let composition = toUpper >=> toWords

    composition "hello world" = process' "hello world" //true

    (toUpper >=> toWords) "hello world" = (toUpper >=> toWords >=> identity) "hello world"
    
[<RequireQualifiedAccess>]
module Async = 

  let singleton value = value |> async.Return

  let bind f x = async.Bind(x, f)

  let apply f x =
    bind (fun f' ->
      bind (fun x' -> singleton(f' x')) x) f

  let map f x = x |> bind (f >> singleton)

  let map2 f x y =
    (apply (apply (singleton f) x) y)

  let map3 f x y z =
    apply (map2 f x y) z

module AsyncOperators =

  let inline (<!>) f x = Async.map f x
  let inline (<*>) f x = Async.apply f x
  let inline (>>=) x f = Async.bind f x

[<RequireQualifiedAccess>]
module Task =
  // To put this idea of a Task as a container into code,
  // I’ve defined its Return, Map, and Bind functions, effectively making Task<T> a monad over T.

  // Bind can be defined trivially in terms of await

  open System.Threading.Tasks
  open FSharp.Parallelx.ContextInsensitive
  
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

module TaskOperators =

  let inline (<!>) f x = Task.map f x
  let inline (<*>) f x = Task.apply f x
  let inline (>>=) x f = Task.bind f x



[<RequireQualifiedAccess>]
module TaskResult = 
  open System.Threading.Tasks
  open Combinators.StructureHelpers.ResultEx
  
  let map f ar =
    Task.map (Result.map f) ar

  let mapError f ar =
    Task.map (Result.mapError f) ar    

  let bind f (ar : Task<_>) = task {
    let! result = ar
    let t = 
      match result with 
      | Ok x -> f x
      | Error e -> task { return Error e }
    return! t      
  }

  let foldResult onSuccess onError ar =
    Task.map (Result.fold onSuccess onError) ar

  let ofAsync aAsync = 
    aAsync
    |> Async.Catch 
    |> Async.StartAsTask 
    |> Task.map Result.ofChoice

  
  let retn x =
    Ok x
    |> Task.singleton
  
  let returnError x =
    Error x
    |> Task.singleton

  let map2 f xR yR =
    Task.map2 (Result.map2 f) xR yR

  let map3 f xR yR zR =
    Task.map3 (Result.map3 f) xR yR zR

  let apply fAR xAR =
    map2 (fun f x -> f x) fAR xAR
    
    
module Functional =
    open System.IO
    open System.Net
    open System.Text
    
    module Async =
        let ``return`` = async.Return
        let bind f a = async.Bind(a, f)
        let (>>=) a f = async.Bind(a, f)

        // Quick test 1
    //    let addWithEase x y = async {
    //        printfn "Will add %i after the break..." x
    //        do! Async.Sleep 1000
    //        return x + y }
    //    (async.Return 0) >>= addWithEase 2 >>= addWithEase (-1)
    //    |> Async.RunSynchronously
    //    |> printfn "%A"
        
        let map f a = async {
            let! a = a
            return f a
        }
        let (<*>) f a =
          async { let! f = f
                  let! a = a
                  return f a }

        let ``pure`` f = async { return f }
        let (<!>) f a = ``pure`` f <*> a

        // Quick test
    //    let delay x = async { do! Async.Sleep 1000
    //                          return x }
    //    ((+) <!> delay 5 <*> delay 4)
    //    |> Async.RunSynchronously
    //    |> printfn "%A"

        // Quick test 2
        let fetchLines (url: string) i = async {
            let rec append i (s: StreamReader) (b: StringBuilder) =
                match i with
                | 0 -> b.ToString()
                | _ -> s.ReadLine() |> b.AppendLine |> append (i-1) s
             
            let req = WebRequest.Create(url) 
            use! resp = req.AsyncGetResponse()
            use stream = resp.GetResponseStream() 
            use reader = new StreamReader(stream) 
            return append i reader (StringBuilder())
        }

        // Downloading content from the internet
        (fun x y z -> String.length x + String.length y + String.length z)
            <!> fetchLines "http://microsoft.github.io" 10
            <*> fetchLines "http://fsharp.org" 10
            <*> fetchLines "http://funscript.info" 10
        |> Async.RunSynchronously
        |> printfn "Chars fetched: %i" 
    
        
        let urls = [ "http://microsoft.github.io"
                     "http://fsharp.org"
                     "http://funscript.info" ]        
    
        let res1 =
            async.Bind(fetchLines urls.[0] 10, fun x ->
                async.Bind(fetchLines urls.[1] 10, fun y ->
                    async.Bind(fetchLines urls.[2] 10, fun z ->
                        async.Return(x + y + z))))
            |> Async.RunSynchronously

        let res2 =
          async {
            let! x = fetchLines urls.[0] 10
            let! y = fetchLines urls.[1] 10
            let! z = fetchLines urls.[2] 10
            return x + y + z }
          |> Async.RunSynchronously    
    
    


[<RequireQualifiedAccess>]
module AsyncResult = 
  open System.Threading.Tasks
  open Combinators.StructureHelpers.ResultEx
  
  let map f ar =
    Async.map (Result.map f) ar

  let mapError f ar =
    Async.map (Result.mapError f) ar    

  let bind f ar = async {
    let! result = ar
    let t = 
      match result with 
      | Ok x -> f x
      | Error e -> async { return Error e }
    return! t      
  }

  let foldResult onSuccess onError ar =
    Async.map (Result.fold onSuccess onError) ar

  let ofTask aTask = 
    aTask
    |> Async.AwaitTask 
    |> Async.Catch 
    |> Async.map Result.ofChoice

  let ofTaskAction (aTask : Task) = 
    aTask
    |> Async.AwaitTask 
    |> Async.Catch 
    |> Async.map Result.ofChoice
  
  let retn x =
    Ok x
    |> Async.singleton
  
  let returnError x =
    Error x
    |> Async.singleton

  let map2 f xR yR =
    Async.map2 (Result.map2 f) xR yR

  let map3 f xR yR zR =
    Async.map3 (Result.map3 f) xR yR zR

  let apply fAR xAR =
    map2 (fun f x -> f x) fAR xAR
    
    
module AsyncArrowModule =
    
    (*
    John Hughes, in his paper Generalising Monads to Arrows, describes an arrow as 'a -> M<'b> where M is any monad.
     We treat this function as a single type until it's time to apply the argument.
     In fact, you can create a type alias for the async arrow like so:
    *)
    
    type AsyncArrow<'a,'b> = 'a -> Async<'b>  // you could use a type Alias
    
    open System.Net.Http
    open System.Text.RegularExpressions
    open System.Diagnostics
    open System.Net
    
    // These aliases are just so that I don't wear out my keyboard.
    type HttpReq = HttpRequestMessage
    type HttpRes = HttpResponseMessage
    
        
    // A few Async functions to make bind and map easy to use
    type Async with
      static member bind (f:'b -> Async<'c>) (a:Async<'b>) : Async<'c> = async.Bind(a, f)
      static member map (f:'b -> 'c) (a:Async<'b>) : Async<'c> = async.Bind(a, f >> async.Return)

    // Don't worry too much about this yet, we'll get to it later
    module AsyncArrow =
      let mapOut (f:'b -> 'c) (a:'a -> Async<'b>) : 'a -> Async<'c> = a >> Async.map f

      let mapOutAsync (f:'b -> Async<'c>) (a:'a -> Async<'b>) : 'a -> Async<'c> = a >> Async.bind f

      let mapIn (f:'a2 -> 'a) (a:'a -> Async<'b>) : 'a2 -> Async<'b> =  f >> a

      let after (f:'a * 'b -> _) (g:'a -> Async<'b>) : 'a -> Async<'b> =
        fun a -> g a |> Async.map (fun b -> let _ = f (a,b) in b)    
    
    
    
    
    
    
    // now that's out of the way, let's write a function that takes a HttpReq and returns an Async<HttpRes>.
    open System.Net
    
    let makeHttpReq : HttpReq -> Async<HttpRes> =
      fun (req:HttpReq) -> async {
        use client = new HttpClient ()
        return! client.SendAsync req |> Async.AwaitTask }
    
    
    // See the HttpReq -> Async<HttpRes> up there? That's an arrow.
    // By the end of this post you'll hopefully be an arrow fan and you'll start seeing the 'a -> M<'b> pattern everywhere.
    
    
//    new HttpReq (HttpMethod.Get, giphyTrending)
//    |> makeHttpRequest
//    |> Async.RunSynchronously
    
    
module private AsyncInterop =
    open System
    open System.Threading
    open System.Threading.Tasks
    open System.Runtime.CompilerServices
    
    
    let asTask(async: Async<'T>, token: CancellationToken option) =
        let tcs = TaskCompletionSource<'T>()
        let token = defaultArg token Async.DefaultCancellationToken
        Async.StartWithContinuations(async,
               tcs.SetResult,
               tcs.SetException,
               tcs.SetException, token)
        tcs.Task

    let asAsync(task: Task, token: CancellationToken option) =
        Async.FromContinuations(    //
            fun (completed, caught, canceled) ->
                let token = defaultArg token Async.DefaultCancellationToken
                task.ContinueWith(new Action<Task>(fun _ ->
                  if task.IsFaulted then caught(task.Exception)
                  else if task.IsCanceled then
                     canceled(new OperationCanceledException(token)|>raise)
                  else completed()), token)
                |> ignore)

    let asAsyncT(task: Task<'T>, token: CancellationToken option) =
        Async.FromContinuations(
            fun (completed, caught, canceled) ->
                let token = defaultArg token Async.DefaultCancellationToken
                task.ContinueWith(new Action<Task<'T>>(fun _ ->
                   if task.IsFaulted then caught(task.Exception)
                   else if task.IsCanceled then
                       canceled(OperationCanceledException(token) |> raise)
                   else completed(task.Result)), token)
                |> ignore)    
    
    
    
    
    
