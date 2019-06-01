module Combinators.AsyncEx.AsyncBuilder

open System
open System.Threading 
open System.Threading.Tasks

module AsyncBuilderEx =
    
    [<RequireQualifiedAccess>]
    module Inference =
        type Defaults =
            | Defaults
            static member Asyncs (x:Async<_>) = x
            static member Asyncs (x:System.Threading.Tasks.Task<_>) = Async.AwaitTask x
        
        let inline defaults (a: ^a, _: ^b) =
            ((^a or ^b) : (static member Asyncs: ^a -> Async<_>) a)
        
        let inline infer (x: ^a) = defaults (x, Defaults)
    let inline infer v = Inference.infer v
        
    type Microsoft.FSharp.Control.AsyncBuilder with   
        [<CustomOperation("and!", IsLikeZip=true)>]
        member __.Merge(x:Async<'a>, y:Async<'b>, [<ProjectionParameter>] resultSelector) =
            async {
                let! x' = Async.StartChild x
                let! y' = Async.StartChild y
                let! x'' = x'
                let! y'' = y'
                return resultSelector x'' y''
            }
        member __.Merge(x:Async<'a>, y:System.Threading.Tasks.Task<'b>, [<ProjectionParameter>] resultSelector) =
            async {
                let x' = Async.StartAsTask x
                do System.Threading.Tasks.Task.WaitAll(x',y)
                return resultSelector x'.Result y.Result
            }
        member __.Merge(x:System.Threading.Tasks.Task<'a>, y:Async<'b>, [<ProjectionParameter>] resultSelector) =
            async {
                let y' = Async.StartAsTask y
                do System.Threading.Tasks.Task.WaitAll(x, y')
                return resultSelector x.Result y'.Result
            }
            

        member inline __.Merge(x, y, [<ProjectionParameter>] resultSelector) =
            async {
                let x' = infer x |> Async.StartAsTask
                let y' = infer y |> Async.StartAsTask
                do System.Threading.Tasks.Task.WaitAll(x',y')
                return resultSelector x'.Result y'.Result
            }

    type Microsoft.FSharp.Control.AsyncBuilder with
        member x.Bind(t : Task<'T>, f : 'T -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)
        member x.ReturnFrom(computation : Task<'T>) = x.ReturnFrom(Async.AwaitTask computation)
        member x.Bind(t : Task, f : unit -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)
        member x.ReturnFrom(computation : Task) = x.ReturnFrom(Async.AwaitTask computation)

        member this.Using(disp:#System.IDisposable, (f:Task<'T> -> Async<'R>)) : Async<'R> =
            this.TryFinally(f disp, fun () ->
                match disp with
                    | null -> ()
                    | disp -> disp.Dispose())