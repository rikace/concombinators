module Combinators.StructureHelpers.LinkedList

[<RequireQualifiedAccess>]
module LinkedList =   
   open System.Collections.Generic
   let empty<'a> = LinkedList<'a>()
   let ofSeq<'a> (xs:'a seq) = LinkedList<'a>(xs)
   let find (f:'a->bool) (xs:LinkedList<'a>) =    
      let node = ref xs.First
      while !node <> null && not <| f((!node).Value) do 
         node := (!node).Next
      !node   
   let findi (f:int->'a->bool) (xs:LinkedList<'a>) =
      let node = ref xs.First
      let i = ref 0
      while !node <> null && not <| f (!i) (!node).Value do 
         incr i; node := (!node).Next
      if !node = null then -1 else !i
   let nth n (xs:LinkedList<'a>) =      
      if n >= xs.Count then 
        let message = "The input sequence has an insufficient number of elements."       
        raise <| new System.ArgumentException(message,paramName="n")
      let node = ref xs.First      
      for i = 1 to n do node := (!node).Next
      !node