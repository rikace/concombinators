module ConCombinators.CallCC

// An implementation of call-with-current-continuation for Async.

// this operation capturs the current continuation and passes it into the current expressin

// callccK    ((a -> K b) -> K a) -> Ka
// callccK h  = fun c -> let k a = fun d -> c a in h k c

// the argument to callccK is a function h, which is passed a function k of type (a -> K b).
// if k is called with argumnet a, it ignires its continuation d and passes a to the captured
//    continuation c instead. 


let callcc (f: ('a -> Async<'b>) -> Async<'a>) : Async<'a> =
  Async.FromContinuations(fun (cont, econt, ccont) ->
    Async.StartWithContinuations(f (fun a -> Async.FromContinuations(fun (_, _, _) -> cont a)), cont, econt, ccont))

  (* Test callcc *)
let sum l =
  let rec sum l = async {
    let! result = callcc (fun exit1 -> async {
      match l with
      | [] -> return 0
      | h::t when h = 2 -> return! exit1 42
      | h::t -> let! r = sum t
                return h + r })
    return result }
  Async.RunSynchronously(sum l)

let ``When summing a list without a 2 via callcc it should return 8``() = sum [1;1;3;3] = 8

let ``When summing a list containing 2 via callcc it should return 43``() = sum [1;2;3] = 43


module CallCC =

  let callcc (f: ('a -> Async<'b>) -> Async<'a>) : Async<'a> =
    Async.FromContinuations(fun (cont, econt, ccont) ->
      Async.StartWithContinuations(f (fun a -> Async.FromContinuations(fun (_, _, _) -> cont a)), cont, econt, ccont))
