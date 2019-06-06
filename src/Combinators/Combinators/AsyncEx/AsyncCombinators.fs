module Combinators.AsyncEx.AsyncCombinators

open Combinators.StructureHelpers.ResultEx

[<RequireQualifiedAccess>]
module Async =
    open System
    open System.Threading 
    open System.Threading.Tasks

    let thenTask (input : Task<'T>, binder : 'T -> Task<'U>) =  
        let tcs = new TaskCompletionSource<'U>()
        input.ContinueWith((fun (task:Task<'T>) ->
           if (task.IsFaulted) then
                tcs.SetException(task.Exception.InnerExceptions)
           elif (task.IsCanceled) then tcs.SetCanceled()
           else
                try
                    (binder(task.Result)).ContinueWith((fun (nextTask:Task<'U>) ->
                        if nextTask.IsFaulted then tcs.SetException(nextTask.Exception.InnerException)
                        elif nextTask.IsCanceled then tcs.SetCanceled()
                        else
                            tcs.SetResult(nextTask.Result))
                            ,TaskContinuationOptions.ExecuteSynchronously) |> ignore  
                with
                | ex -> tcs.SetException(ex)), TaskContinuationOptions.ExecuteSynchronously) |> ignore
        tcs.Task
        

    
    let unit = async.Zero ()
    // x:'a -> Async<'a>
    let retn x = async.Return x

    // f:('b -> Async<'c>) -> a:Async<'b> -> Async<'c>
    let bind (f:'b -> Async<'c>) (a:Async<'b>) : Async<'c> = async.Bind(a, f)

    // map:('a -> 'b) -> value:Async<'a> -> Async<'b>
    let fmap (map : 'a -> 'b) (value : Async<'a>) : Async<'b> = async.Bind(value, map >> async.Return)

    let join (value:Async<Async<'a>>) : Async<'a> = async.Bind(value, id)
    
    let ``pure`` (value:'a) = async.Return value   

    let apply' f x =
      bind (fun f' ->
        bind (fun x' -> retn(f' x')) x) f
    
    // funAsync:Async<('a -> 'b)> -> opAsync:Async<'a> -> Async<'b>
    let apply (funAsync:Async<'a -> 'b>) (opAsync:Async<'a>) = async {
        // We start both async task in Parallel
        let! funAsyncChild = Async.StartChild funAsync 
        let! opAsyncChild = Async.StartChild opAsync

        let! funAsyncRes = funAsyncChild
        let! opAsyncRes = opAsyncChild   
        return funAsyncRes opAsyncRes
        }

    let map (map : 'a -> 'b) (value : Async<'a>) : Async<'b> = async.Bind(value, map >> async.Return)

    let lift f x = map f x
    let lift2 f x y = apply (map f x) y
    let lift3 f x y z = apply (apply (map f x) y) z
    
//    let lift2 (func:'a -> 'b -> 'c) (asyncA:Async<'a>) (asyncB:Async<'b>) =
//        func <!> asyncA <*> asyncB
//    let lift3 (func:'a -> 'b -> 'c -> 'd) (asyncA:Async<'a>) (asyncB:Async<'b>) (asyncC:Async<'c>) =
//        func <!> asyncA <*> asyncB <*> asyncC

    let tee (fn:'a -> 'b) (x:Async<'a>) = (map fn x) |> Async.Ignore|> Async.Start; x
   
    let applyR fR xR =
            fR |> bind (fun f -> xR |> bind (fun x -> retn (f x)))
            
    let map2 f x y =
        (y |> apply (x |> apply (retn f)))
     
    let map3 f x y z =
        apply (map2 f x y) z    

    let sequence seq =
        let inline cons a b = lift2 (fun x xs -> x :: xs)  a b
        List.foldBack cons seq (retn [])

    // f:('a -> Async<'b>) -> x:'a list -> Async<'b list>
    let mapM f x = sequence (List.map f x)

    // xsm:Async<#seq<'b>> * f:('b -> 'c) -> Async<seq<'c>>
    let asyncFor(operations: #seq<'a> Async, f:'a -> 'b) =
        map (Seq.map map) operations
    
    let inline kleisli (f:'a -> Async<'b>) (g:'b -> Async<'c>) (x:'a) = bind g (f x)  // (f x) >>= g   
        
    let combine a b = bind (fun () -> b) a
    
    let dimap (g:'c -> 'a) (h:'b -> 'd) (f:'a -> Async<'b>) : 'c -> Async<'d> =
        g >> f >> map h
    
    let mapInput (g:'c -> 'a) (f:'a -> Async<'b>) : 'c -> Async<'b> =
        g >> f
    
    let mapOut (h:'a * 'b -> 'c) (f:'a -> Async<'b>) : 'a -> Async<'c> =
        fun a -> map (fun b -> h (a,b)) (f a)
    
    let mapOutAsync (h:'a * 'b -> Async<'c>) (f:'a -> Async<'b>) : 'a -> Async<'c> =
        fun a -> bind (fun b -> h (a,b)) (f a)
        
    let inline awaitPlainTask (task: Task) = 
        let continuation (t : Task) : unit =
            match t.IsFaulted with
            | true -> raise t.Exception
            | arg -> ()
        task.ContinueWith continuation |> Async.AwaitTask
 
    let unblockViaNewThread f = async { 
        do! Async.SwitchToNewThread ()
        let res = f()
        do! Async.SwitchToThreadPool ()
        return res }
        
    /// Start non-generic task but don't wait 
    let doTaskAsync:(Task -> unit) = awaitPlainTask >> Async.Start
    /// Start non-generic task and wait
    let doTask:(Task -> unit) = awaitPlainTask >> Async.RunSynchronously    

            
    /// Map an async producing function over a list to get a new Result 
    /// using applicative style
    /// ('a -> Async<'b>) -> 'a list -> Async<'b list>
    let rec traverseA f list =
        // define the applicative functions
        let (<*>) = apply
        // define a "cons" function
        let cons head tail = head :: tail
        // loop through the list
        match list with
        | [] -> 
            // if empty, lift [] to a Result
            retn []
        | head::tail ->
            // otherwise lift the head to a Result using f
            // and cons it with the lifted version of the remaining list
            retn cons <*> (f head) <*> (traverseA f tail)
            
    let traverseAsyncA f list =
        let (<*>) = apply
        let retn = retn

        let cons head tail = head :: tail
        let initState = retn []

        let folder head tail =
            retn cons <*> (f head) <*> tail

        List.foldBack folder list initState            
            
    let traversA' f list =
        let (<*>) = apply
        // define a "cons" function
        let cons head tail = head :: tail
        // right fold over the list
        let initState = retn []
        let folder head tail = 
            retn cons <*> (f head) <*> tail
        List.foldBack folder list initState              
     
        
    let parallelCatch (computations : seq<Async<'a>>) =
        computations
        |> Seq.map Async.Catch
        |> Seq.map (map Result.ofChoice)
        |> Async.Parallel

    let traverse f list =
        let (<*>) = apply
        let folder x xs = retn (fun x xs -> x :: xs) <*> f x <*> xs
        List.foldBack folder list (retn []) 


    let rec tryFind (f : 'T -> Async<bool>) (ts : 'T list) = async {
        match ts with
        | [] -> return None
        | head :: tail ->
            let! r = f head
            if r then return Some head
            else return! tryFind f tail
    }

module AsyncOperators =
    // ( <*> ) : f:Async<('a -> 'b)> -> m:Async<'a> -> Async<'b>
    let (<*>) = Async.apply
    // <!> : f:('a -> 'b) -> m:Async<'a> -> Async<'b>
    let (<!>) = Async.map

    let (<^>) = Async.``pure``

    // Bind
    // operation: value:Async<'a> -> ('a -> Async<'b>) -> Async<'b>
    let inline (>>=) (item:Async<'a>) (operation:'a -> Async<'b>) = Async.bind operation item

    // Kliesli
    // val ( >=> ) : fAsync:('a -> Async<'b>) -> gAsync:('b -> Async<'c>) -> arg:'a -> Async<'c>
    let (>=>) (fAsync:'a -> Async<'b>) (gAsync:'b -> Async<'c>) (arg:'a) = async {
        let! f = Async.StartChild (fAsync arg)
        let! result = f
        return! gAsync result }
