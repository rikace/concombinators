module Kleisli

    let retn x = async { return x }

    let bind (operation:'a -> Async<'b>) (xAsync:Async<'a>) = async {
        let! x = xAsync
        return! operation x }

    let (>>=) (item:Async<'a>) (operation:'a -> Async<'b>) = bind operation item

    
    let run continuation op = Async.StartWithContinuations(op, continuation, (ignore), (ignore))
    
    
    
    let kleisli (f:'a -> Async<'b>) (g:'b -> Async<'c>) (x:'a) = (f x) >>= g

    let (>=>) (operation1:'a -> Async<'b>) 
              (operation2:'b -> Async<'c>) (value:'a) =
                    operation1 value >>= operation2


