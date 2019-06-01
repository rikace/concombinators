module Combinators.StructureHelpers.ResultEx

[<RequireQualifiedAccess>]    
module Result =
    open System
        
    let tryCatch(func: Func<_>) : Result<'TSuccess,exn> =        
        try
            Ok(func.Invoke())
        with
        | exn -> Error exn

    // Signature: ('a -> 'b) -> Result<'a> -> Result<'b>                
    let map f xResult =
        match xResult with
        | Ok x -> Ok (f x)
        | Error errs -> Error errs
    
    // "return" is a keyword in F#, so abbreviate it
    let retn x = Ok x
    
    let bind f xResult =
        match xResult with
        | Ok x -> f x
        | Error errs -> Error errs
        
    let inline either fSuccess fFailure trialResult = 
        match trialResult with
        | Ok(x) -> fSuccess (x)
        | Error(msgs) -> fFailure (msgs)

    let apply fResult xResult =
        match fResult,xResult with
        | Ok f, Ok x ->
            Ok (f x)
        | Error errs, Ok x ->
            Error errs
        | Ok f, Error errs ->
            Error errs
        | Error errs1, Error errs2 ->
            // concat both lists of errors
            Error (List.concat [errs1; errs2])
    // Signature: Result<('a -> 'b)> -> Result<'a> -> Result<'b>

    let apply' f x =
        Result.bind (fun f' ->
          Result.bind (fun x' -> Ok (f' x')) x) f

    // Signature: ('a -> Result<'b>) -> Result<'a> -> Result<'b>
    let inline flatten (result : Result<Result<_,_>,_>) =
        result |> bind id        

    /// Maps a function over the existing error messages in case of failure. In case of success, the message type will be changed and warnings will be discarded.
    let inline mapFailure f result =
        match result with
        | Ok (v) -> Ok v
        | Error errs -> Error (f errs)
        
    let inline lift f result = apply (Ok f) result
    let inline lift2 f a b = apply(map f a) b
    
    let map2 f x y =
        (apply (apply (Ok f) x) y)
      
    let map3 f x y z =
        apply (map2 f x y) z
    
    let fold onOk onError r =
        match r with
        | Ok x -> onOk x
        | Error y -> onError y
    
    let inline ofChoice choice =
        match choice with
        | Choice1Of2 v -> Ok v
        | Choice2Of2 v -> Error v

    let inline toChoice result =
        match result with
        | Ok v -> Choice1Of2 v 
        | Error v -> Choice2Of2 v 

    let traverse f list =
        // define the applicative functions
        let (<*>) = apply
        let retn = retn
    
        // define a "cons" function
        let cons head tail = head :: tail
    
        // right fold over the list
        let initState = retn []
        let folder head tail = 
            retn cons <*> f head <*> tail
    
        List.foldBack folder list initState 
            
    /// Map a Result producing function over a list to get a new Result 
    /// using applicative style
    /// ('a -> Result<'b>) -> 'a list -> Result<'b list>
    let rec traverseResultA f list =

        // define the applicative functions
        let (<*>) = apply
        let retn = Ok

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
            retn cons <*> (f head) <*> (traverseResultA f tail)

    let traverseResultA' f list =
    
        // define the applicative functions
        let (<*>) = apply
        let retn = Ok
    
        // define a "cons" function
        let cons head tail = head :: tail
    
        // right fold over the list
        let initState = retn []
        let folder head tail = 
            retn cons <*> (f head) <*> tail
    
        List.foldBack folder list initState 

    /// Transform a "list<Result>" into a "Result<list>"
    /// and collect the results using apply
    /// Result<'a> list -> Result<'a list>
    let sequenceResultA x = traverseResultA id x
