module Combinators.StructureHelpers.ImmutableDict

module ImmutableDict =
   open System.Collections.Generic
   open System.Collections.Immutable

   /// The empty dictionary.
   let empty<'Key,'T> = 
      ImmutableDictionary<'Key,'T>.Empty :> IImmutableDictionary<'Key,'T>

   /// Tests whether the collection has any elements.
   let isEmpty (table:IImmutableDictionary<'Key,'T>) =
      table.Count = 0

   /// Returns a new dictionary with the binding added to the given dictionary.
   let add key value (table:IImmutableDictionary<'Key,'T>) = 
      table.Add(key,value)
   
   /// Removes an element from the collection.
   let remove key (table:IImmutableDictionary<'Key,'T>) = 
      table.Remove(key)
      
   /// Tests if an element is in collection.
   let containsKey key (table:IImmutableDictionary<'Key,'T>) = 
      table.ContainsKey(key)
   
   /// Looks up an element in the collection.
   let find key (table:IImmutableDictionary<'Key,'T>) = 
      match table.TryGetKey(key) with
      | true, value -> value
      | false, _ -> raise (KeyNotFoundException())
   
   /// Looks up an element in the collection, returning 
   /// a Some value if the element is in the domain of the map, 
   /// or None if not.
   let tryFind key (table:IImmutableDictionary<'Key,'T>) =
      match table.TryGetKey(key) with
      | true, value -> Some value
      | false, _ -> None

   /// Returns an array of all key/value pairs in the mapping. 
   let toArray (table:IImmutableDictionary<'Key,'T>) =
      [|for pair in table -> pair.Key, pair.Value|]

   /// Returns a new collection made from the given bindings.
   let ofSeq (elements:('Key * 'T) seq) =
      seq { for (k,v) in elements -> KeyValuePair(k,v) }
      |> empty.AddRange
     
   /// Creates a new collection whose elements are the results of applying 
   /// the given function to each of the elements of the collection. 
   let map (mapping:'Key -> 'T -> 'U) (table:IImmutableDictionary<'Key,'T>) =
      seq { for pair in table -> KeyValuePair<'Key,'U>(pair.Key, mapping pair.Key pair.Value) }
      |> empty.AddRange
   
   /// Creates a new collection containing only the bindings for which the given predicate returns true.
   let filter (predicate:'Key -> 'T -> bool) (table:IImmutableDictionary<'Key,'T>) =
      seq { for pair in table do if predicate pair.Key pair.Value then yield pair }
      |> empty.AddRange

   /// Returns true if the given predicate returns true for one of the bindings in the collection.
   let exists (predicate:'Key -> 'T -> bool) (table:IImmutableDictionary<'Key,'T>) =
      table |> Seq.exists (fun pair -> predicate pair.Key pair.Value)

   /// Returns true if the given predicate returns true for all of the bindings in the collection.
   let forall (predicate:'Key -> 'T -> bool) (table:IImmutableDictionary<'Key,'T>) =
      table |> Seq.forall (fun pair -> predicate pair.Key pair.Value)

   /// Evaluates the function on each mapping in the collection. 
   /// Returns the key for the first mapping where the function returns true.
   let findKey (predicate:'Key -> 'T -> bool) (table:IImmutableDictionary<'Key,'T>) =
      table |> Seq.find (fun pair -> predicate pair.Key pair.Value) |> fun pair -> pair.Key

   /// Evaluates the function on each mapping in the collection. 
   /// Returns the key for the first mapping where the function returns true.
   let pick (chooser:'Key -> 'T -> 'U option) (table:IImmutableDictionary<'Key,'T>) =
      table |> Seq.pick (fun pair -> chooser pair.Key pair.Value)

   /// Searches the collection looking for the first element where the given function 
   /// returns a Some value.
   let tryPick (chooser:'Key -> 'T -> 'U option) (table:IImmutableDictionary<'Key,'T>) =
      table |> Seq.tryPick (fun pair -> chooser pair.Key pair.Value)

   /// Applies the given function to each binding in the collection
   let iter (action:'Key -> 'T -> unit) (table:IImmutableDictionary<'Key,'T>) =
      table |> Seq.iter (fun pair -> action pair.Key pair.Value)
  
   /// Folds over the bindings in the collection
   let fold (folder:'State -> 'Key -> 'T -> 'State) (state:'State) (table:IImmutableDictionary<'Key,'T>) =
      table |> Seq.fold (fun acc pair -> folder acc pair.Key pair.Value) state