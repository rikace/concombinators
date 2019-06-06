module Combinators.EventEx.DataStreams

  type PushStream<'T> = ('T -> bool) -> bool
  

  // Pushes a value to a receiver
  let inline push   r v = if r v then true else false
  // Starts a push stream
  let inline start  s r = if s r then true else false

  let inline empty          _ = true
  let inline singleton  v   r = push r v


  let inline (>>.)  _ v = v

  // Sources    
  let rec    ofList     vs  r = match vs with [] -> true | h::t -> push r h && ofList t r
  let rec    range      b e r = if b <= e then push r b && range (b + 1) e r else true
  
  // Pipes
  let inline append     f s r = start f r && start s r
  let inline choose     c s r = start s <| fun v -> match c v with Some v -> push r v | _ -> true
  let inline collect    c s r = start s <| fun v -> start (c v) r
  let inline filter     f s r = start s <| fun v -> if f v then push r v else true
  let inline map        m s r = start s <| fun v -> push r (m v)

  // Derived pipes
  let inline concat       s   = collect id s

  // Sinks
  let inline isEmpty        s = start s (fun _ -> false)
  let inline fold       f z s = let a = ref z in start s (fun v -> a := f !a v; true) >>. !a
  let inline tryFirst       s = let a = ref None in start s (fun v -> a := Some v; false) >>. !a

  // Derived sinks
  let inline sum            s = fold (+) LanguagePrimitives.GenericZero s
  let inline toList         s = fold (fun s v -> v::s) [] s |> List.rev
  
  
  let test n =
      range           0 (n - 1)
      |> map          int64
      |> filter       (fun v -> (v &&& 1L) = 0L)
      |> map          ((+) 1L)
      |> sum
      
  printfn "%d" (test 42)
  
