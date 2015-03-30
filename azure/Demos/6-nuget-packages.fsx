﻿#load "credentials.fsx"

open System
open System.IO
open MBrace
open MBrace.Azure
open MBrace.Azure.Client
open MBrace.Workflows
open MBrace.Flow


(**
 This tutorial illustrates using other nuget packages.  You download and reference the packages as normal
 in your F# scripting, and the DLLs for the packages are automatically uploaded to the cloud workers
 as needed.

 In this sample, we use paket (http://fsprojects.fsharp.io/paket) as the tool to fetch packages from NuGet.
 You can alternatively just reference any DLLs you like using normal nuget commands.

 Later in the tutorial you learn how to get native binaries to target machines should you need to do this.
  
 Before running, edit credentials.fsx to enter your connection strings.
**)


//------------------------------------------
// Step 0. Get the package bootstrap. This is standard F# boiler plate for scripts that also get packages.

Directory.CreateDirectory( __SOURCE_DIRECTORY__ + "/script-packages")
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__ + "/script-packages"

if not (File.Exists "paket.exe") then
    let url = "https://github.com/fsprojects/Paket/releases/download/0.27.2/paket.exe" in use wc = new System.Net.WebClient() in let tmp = Path.GetTempFileName() in wc.DownloadFile(url, tmp); File.Move(tmp,"paket.exe");;

//------------------------------------------
// Step 1. Resolve and install the Math.NET Numerics packages. You 
// can add any additional packages you like to this step.

#r "script-packages/paket.exe"

Paket.Dependencies.Install """
    source https://nuget.org/api/v2
    nuget MathNet.Numerics
    nuget MathNet.Numerics.FSharp
    nuget MathNet.Numerics.MKL.Win-x64
""";;


//------------------------------------------
// Step 2. Reference and use the packages on the local machine

#load @"script-packages/packages/MathNet.Numerics.FSharp/MathNet.Numerics.fsx"

open MathNet.Numerics
open MathNet.Numerics.LinearAlgebra

let m1 = Matrix<double>.Build.Random(10,10)
let v1 = Vector<double>.Build.Random(10)

v1 * m1 

(m1 * m1.Inverse()).Determinant()

//------------------------------------------
// Step 3. Run the code on MBrace. Note that the DLLs from the packages are uploaded
// automatically.

// First connect to the cluster
let cluster = Runtime.GetHandle(config)

cluster.ShowProcesses()
cluster.ShowWorkers()

// Invert 100 150x150 matrices using managed code
let managedMathJob = 
    [| 1 .. 100 |]
    |> CloudFlow.ofArray
    |> CloudFlow.map (fun i -> 
            Control.UseManaged()
            let m = Matrix<double>.Build.Random(200,200) 
            (m * m.Inverse()).Determinant())
    |> CloudFlow.sum
    |> cluster.CreateProcess

// Show the progress
managedMathJob.ShowInfo()


// Await the result, we expect ~100.0
let managedMathResults = managedMathJob.AwaitResult()


//------------------------------------------
// Step 4. Run the code on MBrace using the MKL native DLLs. Note that 
// for the moment we manage the upload of the native DLLs explicitly, placing
// them in the temporary storage on the worker.  


cluster.ShowProcesses()
cluster.ShowWorkers()


// To upload DLLs, register their paths as native dependencies
// These will be included with all uploaded dependencies of the session
let contentDir = __SOURCE_DIRECTORY__ + @"/script-packages/packages/MathNet.Numerics.MKL.Win-x64/content/"
Runtime.RegisterNativeDependency <| contentDir + "libiomp5md.dll"
Runtime.RegisterNativeDependency <| contentDir + "MathNet.Numerics.MKL.dll"
Runtime.NativeDependencies

let UseNative() = Control.UseNativeMKL()

// This can take a while first time you run it, because 'MathNet.Numerics.MKL.dll' is 41MB and needs to be uploaded
let firstMklJob = 
   cloud { UseNative()
           let m = Matrix<double>.Build.Random(200,200) 
           return m.LU().Determinant }
    |> cluster.CreateProcess

firstMklJob.ShowInfo()
firstMklJob.AwaitResult()

// 1000 200x200 matrices, inverted using the MKL implementation
let nativeMathJob = 
    cloud { 
        let! r = 
            [| 1 .. 1000 |]
            |> CloudFlow.ofArray
            |> CloudFlow.map (fun i -> 
                  UseNative()
                  let m = Matrix<double>.Build.Random(200,200) 
                  (m * m.Inverse()).Determinant())
            |> CloudFlow.sum
        return r / 1000.0 }
    |> cluster.CreateProcess



nativeMathJob.ShowInfo()
cluster.ShowWorkers()
cluster.ShowProcesses()

nativeMathJob.AwaitResult()

let timeNative  = nativeMathJob.ExecutionTime.TotalSeconds / 1000.0 
let timeManaged = managedMathJob.ExecutionTime.TotalSeconds / 100.0  

timeManaged/timeNative