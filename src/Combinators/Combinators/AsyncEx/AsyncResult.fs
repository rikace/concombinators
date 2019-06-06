module Combinators.AsyncEx.AsyncResult


module AsyncHandler =

    open System
    open Combinators.StructureHelpers.ResultEx
    open Combinators.AsyncEx.AsyncCombinators
    
    [<NoComparison;NoEquality>]    
    type AsyncResult<'a> = | AR of Async<Result<'a, exn>>    
    with
        static member ofChoice value =
            match value with
            | Choice1Of2 value -> Result.Ok value
            | Choice2Of2 e -> Result.Error e

        static member ofOption optValue =
            match optValue with
            | Some value -> Result.Ok value
            | None -> Result.Error (Exception())

    let ofAsyncResult (AR x) = x
    let arr f = AR f

    [<RequireQualifiedAccess>]
    module AsyncResult =     
        let handler operation = AR <| async {
            let! result = Async.Catch operation
            return result |> AsyncResult<_>.ofChoice }
                
        // Implementation of mapHanlder Async-Combinator
        let mapHandler (continuation:'a -> Async<'b>)  (comp : AsyncResult<'a>) = async {
            //Evaluate the outcome of the first future
            let! result = ofAsyncResult comp  
            // Apply the mapping on success
            match result with
            | Result.Ok r -> return! handler (continuation r) |> ofAsyncResult 
            | Result.Error e -> return Result.Error e      
        }
        
        let wrap (computation:Async<'a>) =
            AR <| async {
                let! choice = (Async.Catch computation)
                return (AsyncResult<'a>.ofChoice choice)
            }
    
        let wrapOptionAsync (computation:Async<'a option>) =
            AR <| async {
                let! choice = computation
                return (AsyncResult<'a>.ofOption choice)
            }

        let value x = 
            wrap (async { return x })
    
        ///Map the success outcome of a future
        let flatMap (f:'a -> AsyncResult<'b>) (future: AsyncResult<'a>) = 
            AR <| async {
                let! outcome = ofAsyncResult future
                match outcome with
                | Result.Ok value -> return! (f >> ofAsyncResult) value
                | Result.Error e -> return (Result.Error e)
            }
    
        let mapAr f = 
            let f' value = wrap (async { return (f value) })
            in flatMap f'

        let compose f g = f >> (flatMap g)

        let retn x = Ok x |> Async.retn |> AR
         
        let map mapper asyncResult =
            asyncResult |> Async.map (Result.map mapper) |> AR
            
    
        let bind (binder : 'a -> AsyncResult<'b>) (asyncResult : AsyncResult<'a>) : AsyncResult<'b> = 
            let fSuccess value = 
                value |> (binder >> ofAsyncResult)
            
            let fFailure errs = 
                errs
                |> Error
                |> Async.retn
            
            asyncResult
            |> ofAsyncResult
            |> Async.bind (Result.either fSuccess fFailure)
            |> AR          
    
        let ofTask aTask = 
            aTask
            |> Async.AwaitTask 
            |> Async.Catch 
            |> Async.map AsyncResult<_>.ofChoice
            |> AR
    
        let map2 f xR yR =
            Async.map2 (Result.map2 f) xR yR
    
        let apply fAR xAR =
            map2 (fun f x -> f x) fAR xAR
            
//        let apply (ap : AsyncResult<'a -> 'b>) (asyncResult : AsyncResult<'a>) : AsyncResult<'b> = async {
//            let! result = asyncResult |> Async.StartChild
//            let! fap = ap |> Async.StartChild
//            let! fapResult = fap
//            let! fResult = result
//            match fapResult, fResult with
//            | Ok ap, Ok result -> return ap result |> Ok
//            | Error err, _
//            | _, Error err -> return Error err    }
        
        let inline lift2 f m1 m2 =
            let (>>=) x f = bind f x
            m1 >>= fun x1 -> m2 >>= fun x2 -> (f x1 x2)
            
        let inline lift3 f m1 m2 m3 =
            let (>>=) x f = bind f x
            m1 >>= fun x1 -> m2 >>= fun x2 -> m3 >>= fun x3 -> (f x1 x2 x3)               

            