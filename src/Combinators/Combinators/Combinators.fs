module Combinators.Combinators

open System
open System.Threading 
open System.Threading.Tasks
open System.Reactive
open Combinators.ResultEx
open Combinators.AsyncEx
open Combinators.Tasks
open Combinators.OptionEx

//    [<RequireQualifiedAccess>]
//    module Inference =
//        type Defaults =
//            | Defaults
//            static member Asyncs (x:Async<_>) = x
//            static member Asyncs (x:System.Threading.Tasks.Task<_>) = Async.AwaitTask x
//        
//        let inline defaults (a: ^a, _: ^b) =
//            ((^a or ^b) : (static member Asyncs: ^a -> Async<_>) a)
//        
//        let inline infer (x: ^a) = defaults (x, Defaults)
//    let inline infer v = Inference.infer v

//        let inline (<!>) f x = Async.map f x
//        let inline (<*>) f x = Async.apply f x
//        let inline (>>=) x f = Async.bind f x
//        let inline (>=>) (f:'a -> Async<'b>) (g:'b -> Async<'c>) (x:'a) = (f x) >>= g
//
//        // fAsync:('a -> Async<'b>) -> gAsync:('b -> Async<'c>) -> arg:'a -> Async<'c>
//        let inline (>==>) fAsync gAsync arg = async {
//            let! f = Async.StartChild (fAsync arg)
//            let! result = f
//            return! gAsync result
//        }
//
//        let compose a b = a >=> b

//  let inline (<!>) f x = Result.map f x
//  let inline (<*>) f x = Result.apply f x
//  let inline (>>=) x f = Result.bind f x


// The combination of bind and return are considered even more powerful than apply and return,
// because if you have bind and return, you can construct map and apply from them, but not vice versa.
#nowarn "1173" 
module Combinators =
    module Ops =
        type Bind = Bind with
            static member (?<-) (_: Bind, ma: Async<'a>, f: 'a -> Async<'b>) = Async.bind f ma
            static member (?<-) (_: Bind, ma: Option<'a>, f: 'a -> Option<'b>) = Option.bind f ma
            static member (?<-) (_: Bind, ma: Result<'a, 'b>, f: 'a -> Result<'c, 'b>) = Result.bind f ma
            static member (?<-) (_: Bind, ma: Task<'a>, f: 'a -> Task<'b>) = Task.bind f ma

        type Map = Map with
            static member (?<!>) (_: Map, ma: Async<'a>, f: 'a -> 'b) = Async.map f ma
            static member (?<!>) (_: Map, ma: Option<'a>, f: 'a -> 'b) =
                    // Option.bind (f >> Option.retn)
                    // map defined in terms of bind and return (Some)
                    Option.map f ma
            static member (?<!>) (_: Map, ma: Result<'a, 'c>, f: 'a -> 'b) = Result.map f ma
            static member (?<!>) (_: Map, ma: Task<'a>, f: 'a -> 'b) = Task.map f ma

        type Apply = Apply with
            static member (?>-) (_: Apply, f:Async<'a -> 'b>, m:Async<'a>) = Async.apply f m
            static member (?>-) (_: Apply, f:Option<'a -> 'b>, m:Option<'a>) = Option.apply f m
            static member (?>-) (_: Apply, f:Result<'a -> 'b, 'c list>, m:Result<'a, 'c list>) = Result.apply f m
            static member (?>-) (_: Apply, f:Task<'a -> 'b>, m:Task<'a>) = Task.apply f m

        type Lift2 = Lift2 with
            static member (?*>-) (_: Lift2, f:'a ->'b -> 'c, a:Async<'a>, b:Async<'b>) = Async.lift2 f a b
            static member (?*>-) (_: Lift2, f:'a ->'b -> 'c, a:Task<'a>, b:Task<'b>) = Task.lift2 f a b

        type Lift3 = Lift3 with
            static member (?*>>-) (_: Lift2, f:'a ->'b -> 'c -> 'd, a:Async<'a>, b:Async<'b>, c:Async<'c>) = Async.lift3 f a b c
            
        type Kliesly = Kliesly with
            static member (?*-) (_: Kliesly, f:'a -> Async<'b>, m:'b -> Async<'c>) = Async.kleisli f m
            static member (?*-) (_: Kliesly, f:'a -> Task<'b>, m:'b -> Task<'c>) = Task.kleisli f m
            static member (?*-) (_: Kliesly, f:'a -> Option<'b>, m:'b -> Option<'c>) = f >> (Option.bind m)   
            
    let inline (>>=) m f = (?<-) Ops.Bind m f
    let inline (<*>) m f = (?<!>) Ops.Map m f
    let inline (<!>) f m = (?>-) Ops.Apply f m
    let inline (>=>) f g = (?*-) Ops.Kliesly f g    
    //let inline (<=<)  g f x   = f x >>= g
    let inline ( *^) f a b = (?*>-) Ops.Lift2 f a b     
    let inline ( **^) f a b c = (?*>>-) Ops.Lift3 f a b c     
      
module Bind_vs_Apply_vs_Map =

    // Async-workflow conditional combinators
    let ifAsync (predicate:Async<bool>) funcA funcB =
        async.Bind(predicate, fun p -> if p then funcA else funcB)

    let notAsync predicate = async.Bind(predicate, not >> async.Return)

    let iffAsync (predicate:Async<'a -> bool>) (context:Async<'a>) = async {
        let! p = Async.apply predicate context
        //let! p = predicate <*> context
        return if p then Some context else None }

    let AND funcA funcB = ifAsync funcA funcB (async.Return false)
    let OR funcA funcB = ifAsync funcA (async.Return true) funcB
    let (<&&>) funcA funcB = AND funcA funcB
    let (<||>) funcA funcB = OR funcA funcB
    
    
//[<AutoOpen>]
//module AsyncResultCombinators =
//
//    let inline AND (funcA:AsyncResult<'a>) (funcB:AsyncResult<'a>) : AsyncResult<_> =
//        asyncResult {
//                let! a = funcA
//                let! b = funcB
//                return (a, b)
//        }
//    let inline OR (funcA:AsyncResult<'a>) (funcB:AsyncResult<'a>) : AsyncResult<'a> =
//        asyncResult {
//            return! funcA
//            return! funcB
//        }
//
//    // funcA:AsyncResult<'a> -> funcB:AsyncResult<'a> -> AsyncResult<'a * 'a>
//    let (<&&>) (funcA:AsyncResult<'a>) (funcB:AsyncResult<'a>) = AND funcA funcB
//    // funcA:AsyncResult<'a> -> funcB:AsyncResult<'a> -> AsyncResult<'a>
//    let (<||>) (funcA:AsyncResult<'a>) (funcB:AsyncResult<'a>) = OR funcA funcB
//
//    let gt value (ar:AsyncResult<'a>) =
//        asyncResult {
//            let! result = ar
//            return result > value
//        }
//
//    let (<|||>) (funcA:AsyncResult<bool>) (funcB:AsyncResult<bool>) =
//        asyncResult {
//            let! rA = funcA
//            match rA with
//            | true -> return! funcB
//            | false -> return false
//        }
//
//    let (<&&&>) (funcA:AsyncResult<bool>) (funcB:AsyncResult<bool>) =
//        asyncResult {
//            let! (rA, rB) = funcA <&&> funcB
//            return rA && rB
//        }
