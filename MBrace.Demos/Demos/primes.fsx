﻿// BEGIN PREAMBLE -- do not evaluate, for intellisense only
#r "Nessos.MBrace.Utils"
#r "Nessos.MBrace.Actors"
#r "Nessos.MBrace.Base"
#r "Nessos.MBrace.Store"
#r "Nessos.MBrace.Client"

open Nessos.MBrace.Client
// END PREAMBLE

// Compile Demos project before adding reference
#r "bin/Debug/Demos.dll"

open Primality
open System.Numerics

//
//  Checking for Mersenne primes using {m}brace
//  a Mersenne prime is a number of the form 2^p - 1 that is prime
//  the library defines a simple checker that uses the Lucas-Lehmer test

#time

// sequential Mersenne Prime Search
let tryFindMersenneSeq ts = Array.tryFind isMersennePrime ts

tryFindMersenneSeq [| 2500 .. 3500 |]


//
//  Nondeterministic approach
//

// Async Choice
[| 2500 .. 3500 |] 
|> Array.map (fun i -> async { return if isMersennePrime i then Some i else None })
|> Async.Choice
|> Async.RunSynchronously



// a general-purpose non-deterministic search combinator for the cloud

[<Cloud>]
let tryFind (f : 'T -> bool) partitionSize (inputs : 'T []) =
    let searchLocal (inputs : 'T []) =
        inputs
        |> Array.map (fun t -> cloud { return if f t then Some t else None })
        |> Cloud.Choice
        |> local // local combinator restricts computation to local async semantics

    cloud {
        return!
            inputs
            |> Array.partition partitionSize
            |> Array.map searchLocal
            |> Cloud.Choice
    }

let runtime = MBrace.InitLocal 4

let proc = runtime.CreateProcess <@ tryFind isMersennePrime 100 [|500 .. 1000|] @>

proc

proc.AwaitResult()
