module Combinators.AsyncEx.ParallelHeterogeneousAsync

type Parallel<'T> =
    private {
        Compute : Async<obj>[]
        Unpack : obj [] -> int -> 'T
    }

    static member ( <*> ) (f: Parallel<'A -> 'B>, x: Parallel<'A>) : Parallel<'B> =
        {
            Compute = Array.append f.Compute x.Compute
            Unpack = fun xs pos ->
                let fv = f.Unpack xs pos
                let xv = x.Unpack xs (pos + f.Compute.Length)
                fv xv
        }

    static member ( <*> ) (f: Parallel<'A ->' B>, x: Async<'A>) : Parallel<'B> =
        f <*> Parallel.Await(x)

and Parallel =

    static member Run<'T> (p: Parallel<'T>) : Async<'T> =
        async {
            let! results =
                match p.Compute.Length with
                | 0 -> async.Return [|box ()|]
                | 1 -> async { let! r = p.Compute.[0] 
                               return [| r |] }
                | _ -> Async.Parallel p.Compute
            return p.Unpack results 0
        }

    static member Await<'T> (x: Async<'T>) : Parallel<'T> =
        {
            Compute =
                [|
                    async {
                        let! v = x
                        return box v
                    }
                |]
            Unpack = fun xs pos -> unbox xs.[pos]
        }

    static member Pure<'T>(x: 'T) : Parallel<'T> =
        {
            Compute = [||]
            Unpack = fun _ _ -> x
        }

let myInt, myChar, myBool, myString =
    Parallel.Pure (fun w x y z -> (w, x, y, z))
    <*> async { return 1 }
    <*> async { return 'b' }
    <*> async { return true }
    <*> async { return "abc" }
    |> Parallel.Run
    |> Async.RunSynchronously

let test1 () =
    Parallel.Pure (fun x y z -> (x, y, z))
    <*> async { return 1 }
        // unit -> Parallel<('b -> 'c -> int * 'b * 'c)>

    <*> async { return 'b' }
        // unit -> Parallel<('c -> int * char * 'c)>

    <*> async { return true }
        // unit -> Parallel<int * char * bool>

    |> Parallel.Run
        // unit -> Async<int * char * bool>

    |> Async.RunSynchronously
        // unit -> int * char * bool
        
test1()        