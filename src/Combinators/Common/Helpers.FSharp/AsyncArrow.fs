namespace FunctionalConcurrency

open System
open System.IO
open System.Threading.Tasks
open System.Threading

module AsyncArrow =
    open AsyncOperators

    let (>>=) m f = Async.bind f m
    
    let unit x = Async.unit x
    
    type AsyncArrow<'a,'b> = AsyncArrow of ('a -> Async<'b>)
    
    // (f:'a -> Async<'b>) = AsyncArrow f
    let arr (f:'a -> Async<'b>) = AsyncArrow f
    
    // f:('a -> 'b) -> AsyncArrow<'a,'b>
    let pure' (f:'a -> 'b) = arr (f >> unit)
    
    // AsyncArrow<'a,'b> -> AsyncArrow<'b,'c> -> AsyncArrow<'a,'c>
    let (>>>>) (AsyncArrow f) (AsyncArrow g) = arr ((fun m -> m >>= g) << f)
    
    // m:AsyncArrow<'a,'b> -> f:('b -> 'c) -> AsyncArrow<'a,'c>
    let mapArrow m f = m >>>> pure' f
    
    // AsyncArrow<unit,'a> -> Async<'a>
    let toMonad (AsyncArrow f) = f()
    
    // m:Async<'a> -> f:('a -> 'b) -> Async<'b>
    let map m f = mapArrow (arr (fun _ -> m)) f |> toMonad
    
    // m:Async<'a> -> f:('a -> Async<'b>) -> Async<'b>
    let bind m f = (arr (fun _ -> m) >>>> arr f) |> toMonad
    
    // f:('a -> 'b) -> m:Async<'a> -> Async<'b>
    let lift f m = m >>= (fun a -> unit (f a))
    
    
