﻿/////////////////////////////////////////////////////////////////////////////////////////////////
//
// FSharp.Control.FusionTasks - F# Async computation <--> .NET Task easy seamless interoperability library.
// Copyright (c) 2016 Kouji Matsui (@kekyo2)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
/////////////////////////////////////////////////////////////////////////////////////////////////

namespace FSharp.Control

open System
open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks

[<AutoOpen>]
module AsyncExtensions =

  ///////////////////////////////////////////////////////////////////////////////////
  // Internal implementations.

  let private (|IsFaulted|IsCanceled|IsCompleted|) (task: Task) =
    if task.IsFaulted then IsFaulted task.Exception
    else if task.IsCanceled then IsCanceled
    else IsCompleted

  let private safeToken (ct: CancellationToken option) =
    match ct with
    | Some token -> token
    | None -> Async.DefaultCancellationToken

  let internal asTask(async: Async<'T>, ct: CancellationToken option) =
    let tcs = TaskCompletionSource<'T>()
    Async.StartWithContinuations(
      async,
      tcs.SetResult,
      tcs.SetException,
      tcs.SetException, // Derived from original OperationCancelledException
      safeToken ct)
    tcs.Task

  let internal asAsync(task: Task, ct: CancellationToken option) =
    Async.FromContinuations(
      fun (completed, caught, canceled) ->
        task.ContinueWith(
          new Action<Task>(fun _ ->
            match task with  
            | IsFaulted exn -> caught(exn)
            | IsCanceled -> canceled(OperationCanceledException()) // TODO: how to extract implicit caught exceptions from task?
            | IsCompleted -> completed(())),
          safeToken ct)
        |> ignore)

  let internal asAsyncT(task: Task<'T>, ct: CancellationToken option) =
    Async.FromContinuations(
      fun (completed, caught, canceled) ->
        task.ContinueWith(
          new Action<Task<'T>>(fun _ ->
            match task with  
            | IsFaulted exn -> caught(exn)
            | IsCanceled -> canceled(OperationCanceledException()) // TODO: how to extract implicit caught exceptions from task?
            | IsCompleted -> completed(task.Result)),
          safeToken ct)
        |> ignore)

  let internal asAsyncCTA(cta: ConfiguredAsyncAwaitable) =
    Async.FromContinuations(
      fun (completed, caught, canceled) ->
        let awaiter = cta.GetAwaiter()
        awaiter.OnCompleted(
          new Action(fun _ ->
            try
              awaiter.GetResult()
              completed()
            with exn -> caught(exn)))
        |> ignore)

  let internal asAsyncCTAT(cta: ConfiguredAsyncAwaitable<'T>) =
    Async.FromContinuations(
      fun (completed, caught, canceled) ->
        let awaiter = cta.GetAwaiter()
        awaiter.OnCompleted(
          new Action(fun _ ->
            try completed(awaiter.GetResult())
            with exn -> caught(exn)))
        |> ignore)

  ///////////////////////////////////////////////////////////////////////////////////
  // F# side Async class extensions.

  type Async with

    /// <summary>
    /// Seamless conversion from F# Async to .NET Task.
    /// </summary>
    /// <param name="async">F# Async</param>
    /// <param name="token">Cancellation token (optional)</param>
    /// <returns>.NET Task</returns>
    static member AsTask(async: Async<unit>, ?token: CancellationToken) = asTask(async, token) :> Task

    /// <summary>
    /// Seamless conversion from F# Async to .NET Task.
    /// </summary>
    /// <typeparam name="'T">Computation result type</typeparam> 
    /// <param name="async">F# Async</param>
    /// <param name="token">Cancellation token (optional)</param>
    /// <returns>.NET Task</returns>
    static member AsTask(async: Async<'T>, ?token: CancellationToken) = asTask(async, token)

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async.
    /// </summary>
    /// <param name="task">.NET Task</param>
    /// <param name="token">Cancellation token (optional)</param>
    /// <returns>F# Async</returns>
    static member AsAsync(task: Task, ?token: CancellationToken) = asAsync(task, token)

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async.
    /// </summary>
    /// <typeparam name="'T">Computation result type</typeparam> 
    /// <param name="task">.NET Task</param>
    /// <param name="token">Cancellation token (optional)</param>
    /// <returns>F# Async</returns>
    static member AsAsync(task: Task<'T>, ?token: CancellationToken) = asAsyncT(task, token)

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async.
    /// </summary>
    /// <param name="cta">.NET ConfiguredTaskAwaitable (expr.ConfigureAwait(...))</param>
    /// <returns>F# Async</returns>
    static member AsAsync(cta: ConfiguredAsyncAwaitable) = asAsyncCTA(cta)

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async.
    /// </summary>
    /// <typeparam name="'T">Computation result type</typeparam> 
    /// <param name="cta">.NET ConfiguredTaskAwaitable (expr.ConfigureAwait(...))</param>
    /// <returns>F# Async</returns>
    static member AsAsync(cta: ConfiguredAsyncAwaitable<'T>) = asAsyncCTAT(cta)

  ///////////////////////////////////////////////////////////////////////////////////
  // F# side async computation builder extensions.

  type AsyncBuilder with

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async in Async workflow.
    /// </summary>
    /// <param name="expr">.NET Task (expression result)</param>
    /// <returns>F# Async</returns>
    member __.Source(expr: Task) = asAsync(expr, None)

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async in Async workflow.
    /// </summary>
    /// <typeparam name="'T">Computation result type</typeparam> 
    /// <param name="expr">.NET Task (expression result)</param>
    /// <returns>F# Async</returns>
    member __.Source(expr: Task<'T>) = asAsyncT(expr, None)

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async in Async workflow.
    /// </summary>
    /// <param name="cta">.NET ConfiguredTaskAwaitable (expr.ConfigureAwait(...))</param>
    /// <returns>F# Async</returns>
    member __.Source(cta: ConfiguredAsyncAwaitable) = asAsyncCTA(cta)

    /// <summary>
    /// Seamless conversion from .NET Task to F# Async in Async workflow.
    /// </summary>
    /// <typeparam name="'T">Computation result type</typeparam> 
    /// <param name="cta">.NET ConfiguredTaskAwaitable (expr.ConfigureAwait(...))</param>
    /// <returns>F# Async</returns>
    member __.Source(cta: ConfiguredAsyncAwaitable<'T>) = asAsyncCTAT(cta)

  ///////////////////////////////////////////////////////////////////////////////////
  // F# side Task class extensions.

  type Task with

    /// <summary>
    /// Seamless conversionable substitution Task.ConfigureAwait()
    /// </summary>
    /// <param name="task">.NET Task</param>
    /// <param name="continueOnCapturedContext">True if continuation running on captured SynchronizationContext</param>
    /// <returns>ConfiguredAsyncAwaitable</returns>
    member task.Configure(continueOnCapturedContext: bool) =
        ConfiguredAsyncAwaitable(task.ConfigureAwait(continueOnCapturedContext))

  type Task<'T> with

    /// <summary>
    /// Seamless conversionable substitution Task.ConfigureAwait()
    /// </summary>
    /// <param name="task">.NET Task&lt;'T&gt;</param>
    /// <param name="continueOnCapturedContext">True if continuation running on captured SynchronizationContext</param>
    /// <returns>ConfiguredAsyncAwaitable</returns>
    member task.Configure(continueOnCapturedContext: bool) =
        ConfiguredAsyncAwaitable<'T>(task.ConfigureAwait(continueOnCapturedContext))
