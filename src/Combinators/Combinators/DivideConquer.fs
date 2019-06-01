module Combinators.DivideConquer

open System.Threading.Tasks

module QuickSort =
    open System

    let rec quicksortParallelWithDepth depth aList =
        match aList with
        | [] -> []
        | firstElement :: restOfList ->
            let smaller, larger =
                List.partition (fun number -> number > firstElement) restOfList
            if depth < 0 then
                let left  = quicksortParallelWithDepth depth smaller
                let right = quicksortParallelWithDepth depth larger
                left @ (firstElement :: right)
            else
                let left  = Task.Run(fun () -> quicksortParallelWithDepth (depth - 1) smaller)
                let right = Task.Run(fun () -> quicksortParallelWithDepth (depth - 1) larger)
                left.Result @ (firstElement :: right.Result)


    let rand = Random(int(DateTime.Now.Ticks))
    let dataSamples =
            List.init 1000000 (fun i -> rand.Next())
    
    let maxDepth = int (Math.Log(float Environment.ProcessorCount, 2.0))
    let sorting = quicksortParallelWithDepth maxDepth dataSamples
    

module ReduceParallel = 
    
    module List = 
      let reduceParallel<'a> f (ie :'a list) =         
        let rec reduceRec (reducer:'a -> 'a -> 'a) (ie :'a list) (len:int) = 
          match len with
          | 1 -> ie.[0]
          | 2 -> f ie.[0] ie.[1]
          | len -> 
            let h = len / 2
            let o1 = Task.Run(fun _ -> reduceRec f (ie |> List.take h) h)
            let o2 = Task.Run(fun _ -> reduceRec f (ie |> List.skip h) (len-h))
            Task.WaitAll(o1, o2)
            f o1.Result o2.Result
        match ie.Length with
        | 0 -> failwith "Sequence contains no elements"
        | c -> reduceRec f ie c

    module Array = 
      let reduceParallel<'a> f (ie :'a array) =
        let rec reduceRec f (ie :'a array) = function
          | 1 -> ie.[0]
          | 2 -> f ie.[0] ie.[1]
          | len -> 
            let h = len / 2
            let o1 = Task.Run(fun _ -> reduceRec f (ie |> Array.take h) h)
            let o2 = Task.Run(fun _ -> reduceRec f (ie |> Array.skip h) (len-h))
            Task.WaitAll(o1, o2)
            f o1.Result o2.Result
        match ie.Length with
        | 0 -> failwith "Sequence contains no elements"
        | c -> reduceRec f ie c

(*
[1 .. 500] |> List.fold(fun a -> fun x -> System.Threading.Thread.Sleep(30);x+a) 0;;
[1 .. 500] |> List.reduceParallel(fun a -> fun x -> System.Threading.Thread.Sleep(30);x+a);;
[|1 .. 500|] |> Array.fold(fun a -> fun x -> System.Threading.Thread.Sleep(30);x+a) 0;;
[|1 .. 500|] |> Array.reduceParallel(fun a -> fun x -> System.Threading.Thread.Sleep(30);x+a);;
*)