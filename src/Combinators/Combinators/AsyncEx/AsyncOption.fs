module Combinators.AsyncEx.AsyncOption

type AsyncOption<'a> = Async<Option<'a>>


[<RequireQualifiedAccess>]
module AsyncOption =
    let handler (operation:Async<'a>) : AsyncOption<'a> = async {
        let! result = Async.Catch operation
        return
          match result with
          | Choice1Of2 value -> Some value
          | Choice2Of2 _ -> None
    }
    
    // Async Option
    let compose w1 w2 ctx = async {
        let! result = w1 ctx
        match result with
        | Some ctx -> return! w2 ctx
        | _ -> return None
    }
    
        