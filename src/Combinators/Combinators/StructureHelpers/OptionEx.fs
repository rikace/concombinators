module Combinators.StructureHelpers.OptionEx

[<RequireQualifiedAccess>]
module Option =
    
    let ofChoice choice =
        match choice with
        | Choice1Of2 value -> Some value
        | Choice2Of2 _ -> None
        
    /// <summary>Converts an option to a Result.</summary>
    /// <param name="source">The option value.</param>
    /// <returns>The resulting Result value.</returns>
    let toResult source = match source with Some x -> Ok x | None -> Error ()
        

    let retn x = Some x
    
    let apply f a =
        match f with
        | Some f -> Option.map f a
        | None -> None

    //let apply' fOpt xOpt =
    //    fOpt |> Option.bind (fun f ->
    //        let map = Option.bind (f >> Some)
    //        map xOpt)

    let ``pure`` f = Some f

    //printfn "%A" ((+) <!> Some 5 <*> Some 4)    // Quick tests
    //printfn "%A" ((+) <!> Some 5 <*> None  )
    //let mul a b = a * b
    //let m = mul <!> (Some 3) <*> (Some 8)
    let joinOption opt =
        match opt with
        | None          -> None
        | Some innerOpt -> innerOpt

    // f:('a -> 'b -> 'c) -> x:'a option -> y:'b option -> 'c option
    let lift2 f x y     = apply (Option.map f x) y
    
    //  f:('a -> 'b -> 'c -> 'd) ->  x:'a option -> y:'b option -> z:'c option -> 'd option
    let lift3 f x y z   = apply (apply (Option.map f x) y) z
        //f <!> x <*> y <*> z
    let lift4 f x y z w = apply(apply (apply (Option.map f x) y) z) w
        //f <!> x <*> y <*> z <*> w

    let filter f opt =
        match opt with
        | None -> None
        | Some x -> if f x then opt else None

    let ofNull<'T when 'T : not struct> (t : 'T) =
        if obj.ReferenceEquals(t, null) then None
        else
            Some t

    /// Returns None if it contains a None element, otherwise a list of all elements
//    let sequence (t: seq<option<'T>>) =
//        let mutable ok = true
//        let res = Seq.toArray (seq {
//            use e = t.GetEnumerator ()
//            while e.MoveNext () && ok do
//                match e.Current with
//                | Some v -> yield v
//                | None   -> ok <- false })
//        if ok then Some (Array.toSeq res) else None

    let sequence (listOfOptions : 'a option list) =
        let folder x xs = apply(apply (retn (fun x xs -> x :: xs)) x) xs
        List.foldBack folder listOfOptions (retn [])
    

    /// <summary>If value is Some, returns both of them tupled. Otherwise it returns None tupled.</summary>
    /// <param name="v">The value.</param>
    /// <returns>The resulting tuple.</returns>
    let unzip v =
        match v with
        | Some (x, y) -> Some x, Some y
        | _           -> None  , None

    /// <summary>If both value are Some, returns both of them tupled. Otherwise it returns None.</summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The second value.</param>
    /// <returns>The resulting option.</returns>
    let zip x y =
        match x, y with
        | Some x, Some y -> Some (x, y)
        | _              -> None

    /// define lift2 from scratch
    let map2 f xOpt yOpt = 
        match xOpt,yOpt with
        | Some x,Some y -> Some (f x y)
        | _ -> None
    
    let traverse f list =
        let folder x xs = apply (apply (retn (fun x xs -> x :: xs)) (f x))  xs
        //let folder x xs = retn (fun x xs -> x :: xs) <*> f x <*> xs
        List.foldBack folder list (retn [])          
    
    // traverse Option
    let traverseOptionA f list =
        let cons head tail = head :: tail
        let initState = retn []

        let folder head tail =
            apply (apply (retn cons) (f head)) tail
            //retn cons <*> (f head) <*> tail

        List.foldBack folder list initState

    
    
    
    type OptionBuilder() =
        member __.Return(value) =
            printfn "maybe.Return(%A)" value
            Some value
        member __.Bind(m, f) =
            printfn "maybe.Bind(%A, %A)" m f
            Option.bind f m
        member __.Zero() =
            printfn "maybe.Zero()"
            Some ()
        member __.ReturnFrom(m:'a option) =
            printfn "maybe.ReturnFrom(%A)" m
            m
        member __.Combine(m:'a option, f:unit -> 'a option) =
            printfn "maybe.Combine(%A, %A)" m f
            match m with
            | Some _ -> m
            | None -> f()
        
        member __.Delay(f:unit -> 'a option) =
            printfn "maybe.Delay(%A)" f
            f()
        
        member __.Combine(m1:'a option, m2:'a option) =
            printfn "maybe.Combine(%A, %A)" m1 m2
            match m1 with
            | Some _ -> m1
            | None -> m2  
                                                          
        member __.Run(f:unit -> 'a option) =
            printfn "maybe.Run(%A)" f
            f()
            
        member __.TryWith(tryBlock, withBlock) =
            try tryBlock()
            with e -> withBlock e            
            
        member __.While(pred, body) =
            if pred() then
                __.Bind(body, (fun () -> __.While(pred,body)))
            else __.Zero()            
            
        member __.Using(disposable:#System.IDisposable, f) =
            try
                f disposable
            finally
                match disposable with
                | null -> ()
                | disp -> disp.Dispose()            
            
        member __.For(items:seq<_>, f) =
            __.Using(items.GetEnumerator(), (fun enum ->
                __.While((fun () -> enum.MoveNext()),
                         __.Delay(fun () -> f enum.Current))))            
            
    let maybe = OptionBuilder()
