module Combinators.SumNumbers

open System
open System.Threading
open System.Threading.Tasks
open Combinators.TaskEx.TaskCombinators
open FSharp.Parallelx.ContextInsensitive

let fork (f : 'a -> Task<'a>) =
    fun x -> task {
    return! f x
}


module Seq = 
    let splitAt length (xs: seq<'T>) =
        Seq.truncate length xs |> Seq.toList, Seq.skip length xs


let rec sum (ints : int seq) =
    if Seq.isEmpty ints then 0
    else
        let left, right = ints |> Seq.splitAt ((ints |> Seq.length) / 2)
        sum left + sum right
        
        

let sumFold (ints : int seq) = (ints, 0) ||> Seq.foldBack(fun item state -> item + state)


let rec sumRec depth (ints : int list) =
    match ints with
    | [] -> 0
    | lst ->
        let left, right = ints |> List.splitAt (lst.Length / 2)
        //sumRec left + sumRec right
        if depth < 0 then 
            let left  = sumRec depth left  
            let right = sumRec depth right   
            left + right
        else
            let left  = Task.Run(fun () -> sumRec (depth - 1) left) 
            let right = Task.Run(fun () -> sumRec (depth - 1) right)  
            left.Result + right.Result


let rec sumRec' depth (ints : int list) = task {
    match ints with
    | [] -> return 0
    | lst ->
        let left, right = ints |> List.splitAt (lst.Length / 2)
        if depth < 0 then 
            let! left  = sumRec' depth left  
            let! right = sumRec' depth right   
            return left + right
        else
            let left  = sumRec' (depth - 1) left 
            let right = sumRec' (depth - 1) right  
            return! TaskV2.map2 (+) left right }    


let taskCPS (op : Task<'a>) (f: 'a -> Task<'b>) = task {
    return! op.ContinueWith(fun (t : Task<'a>) ->
         f t.Result)
}
