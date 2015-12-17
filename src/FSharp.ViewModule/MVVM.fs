﻿(*
Copyright (c) 2013-2015 FSharp.ViewModule Team

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*)

namespace FSharp.ViewModule

open System
open System.ComponentModel
open System.Collections.Generic
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
open System.Threading
open System.Windows.Input

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns

open FSharp.ViewModule
open FSharp.ViewModule.Validation

/// Encapsulation of a value which handles raising property changed automatically in a clean manner
type public INotifyingValue<'a> =
    inherit IObservable<'a>
    /// Extracts the current value from the backing storage
    abstract member Value : 'a with get, set

type public NotifyingValue<'a>(defaultValue) =
    let mutable value = defaultValue
    let ev = Event<'a>()
        
    member __.Value 
        with get() = value 
        and set(v) = 
            if (not (EqualityComparer<'a>.Default.Equals(value, v))) then
                value <- v
                ev.Trigger v

    interface IObservable<'a> with
        member this.Subscribe observer =
            let obs = ev.Publish :> IObservable<'a>
            obs.Subscribe observer

    interface INotifyingValue<'a> with
        member this.Value with get() = this.Value and set(v) = this.Value <- v    

type IViewModelPropertyFactory =
    abstract Backing<'a> : prop:Expr * defaultValue:'a * validate:(ValidationResult<'a> -> ValidationResult<'a>) -> INotifyingValue<'a>
    abstract Backing<'a> : prop:Expr * defaultValue:'a * ?validate:('a -> string list) -> INotifyingValue<'a>
    abstract FromFuncs<'a> : prop:Expr * getter:(unit->'a) * setter: ('a->unit) -> INotifyingValue<'a>

    abstract CommandAsync : asyncWorkflow:(SynchronizationContext -> Async<unit>) * ?token:CancellationToken * ?onCancel:(OperationCanceledException -> unit) -> IAsyncNotifyCommand
    abstract CommandAsyncChecked : asyncWorkflow:(SynchronizationContext -> Async<unit>) * canExecute:(unit -> bool) * ?dependentProperties: Expr list * ?token:CancellationToken * ?onCancel:(OperationCanceledException -> unit) -> IAsyncNotifyCommand
    abstract CommandAsyncParam<'a> : asyncWorkflow:(SynchronizationContext -> 'a -> Async<unit>) * ?token:CancellationToken * ?onCancel:(OperationCanceledException -> unit) -> IAsyncNotifyCommand
    abstract CommandAsyncParamChecked<'a> : asyncWorkflow:(SynchronizationContext -> 'a -> Async<unit>) * canExecute:('a -> bool) * ?dependentProperties: Expr list * ?token:CancellationToken * ?onCancel:(OperationCanceledException -> unit) -> IAsyncNotifyCommand    

    abstract CommandSync : execute:(unit -> unit) -> INotifyCommand
    abstract CommandSyncParam<'a> : execute:('a -> unit) -> INotifyCommand
    abstract CommandSyncChecked : execute:(unit -> unit) * canExecute:(unit -> bool) * ?dependentProperties: Expr list -> INotifyCommand
    abstract CommandSyncParamChecked<'a> : execute:('a -> unit) * canExecute:('a -> bool) * ?dependentProperties: Expr list -> INotifyCommand    

type IEventViewModelPropertyFactory<'a> =
    inherit IViewModelPropertyFactory

    abstract EventValueCommand<'a> : value:'a -> ICommand
    abstract EventValueCommand<'a> : unit -> ICommand
    abstract EventValueCommand<'a,'b> : valueFactory:('b -> 'a) -> ICommand

    abstract EventValueCommandChecked<'a> : value:'a * canExecute:(unit -> bool) * ?dependentProperties: Expr list -> INotifyCommand
    abstract EventValueCommandChecked<'a> : canExecute:('a -> bool) * ?dependentProperties: Expr list -> INotifyCommand
    abstract EventValueCommandChecked<'a,'b> : valueFactory:('b -> 'a) * canExecute:('b -> bool) * ?dependentProperties: Expr list -> INotifyCommand

type internal NotifyingValueBackingField<'a> (propertyName, raisePropertyChanged : string -> unit, storage : NotifyingValue<'a>, validationResultPublisher : IValidationTracker, validate : 'a -> string list) =    
    let value = storage
    
    let updateValidation () =
        validate value.Value
    
    do
        value.Add (fun _ -> raisePropertyChanged propertyName)

        validationResultPublisher.AddPropertyValidationWatcher propertyName updateValidation
        if (SynchronizationContext.Current <> null) then
            SynchronizationContext.Current.Post((fun _ -> validationResultPublisher.Revalidate propertyName), null)

    new(propertyName, raisePropertyChanged : string -> unit, defaultValue : 'a, validationResultPublisher, validate) =
        NotifyingValueBackingField<'a>(propertyName, raisePropertyChanged, NotifyingValue<_>(defaultValue), validationResultPublisher, validate)

    member __.Value 
        with get() = value.Value
        and set(v) = value.Value <- v

    interface IObservable<'a> with
        member this.Subscribe observer = (value :> IObservable<'a>).Subscribe observer

    interface INotifyingValue<'a> with
        member this.Value with get() = this.Value and set(v) = this.Value <- v


type internal NotifyingValueFuncs<'a> (propertyName, raisePropertyChanged : string -> unit, getter, setter) =
    let propertyName = propertyName
    let ev = Event<'a>()

    do
        ev.Publish.Add (fun _ -> raisePropertyChanged propertyName)
    
    member this.Value 
        with get() = getter()
        and set(v) = 
            if (not (EqualityComparer<'a>.Default.Equals(getter(), v))) then
                setter v
                ev.Trigger v
        
    interface IObservable<'a> with
        member this.Subscribe observer =
            let obs = ev.Publish :> IObservable<'a>
            obs.Subscribe observer

    interface INotifyingValue<'a> with
        member this.Value with get() = this.Value and set(v) = this.Value <- v
    
namespace FSharp.ViewModule.Internal
open System
open System.ComponentModel
open System.Collections.Generic
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
open System.Threading
open System.Windows.Input

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns

open FSharp.ViewModule
open FSharp.ViewModule.Validation


[<AbstractClass>]
type ViewModelUntyped() as self =
    let propertyChanged = new Event<_, _>()
    let depTracker = DependencyTracker(self.RaisePropertyChanged, propertyChanged.Publish)
    let dependencyTracker = depTracker :> IDependencyTracker

    // Used for error tracking
    let errorRelatedProperties = [ <@@ self.EntityErrors @@> ; <@@ self.PropertyErrors @@> ; <@@ self.IsValid @@> ]
    let errorRelatedPropertyNames = errorRelatedProperties |> List.map getPropertyNameFromExpression

    let errorsChanged = new Event<EventHandler<DataErrorsChangedEventArgs>, DataErrorsChangedEventArgs>()
    let errorTracker = ValidationTracker(self.RaiseErrorChanged, propertyChanged.Publish, self.Validate, errorRelatedProperties)
    let validationTracker = errorTracker :> IValidationTracker

    let getExecuting () = self.OperationExecuting
    let setExecuting b = self.OperationExecuting <- b
    
    let addCommandDependencies cmd dependentProperties =
        let deps : Expr list = defaultArg dependentProperties []
        deps |> List.iter (fun prop -> dependencyTracker.AddCommandDependency(cmd, prop)) 

    // TODO: This should be set by commands to allow disabling of other commands by default
    let propChanged : string -> unit = self.RaisePropertyChanged
    let operationExecuting = NotifyingValueBackingField(getPropertyNameFromExpression(<@ self.OperationExecuting @>), propChanged, false, validationTracker, (fun _ -> List.empty)) :> INotifyingValue<bool>

    // Overridable entity level validation
    abstract member Validate : string -> ValidationResult seq
    default this.Validate(propertyName: string) =
        Seq.empty
            
    member private this.RaiseErrorChanged(propertyName : string) =
        errorsChanged.Trigger(this, new DataErrorsChangedEventArgs(propertyName))
        if (Option.isNone(errorRelatedPropertyNames |> List.tryFind ((=) propertyName))) then
            errorRelatedPropertyNames
            |> List.iter (fun p -> this.RaisePropertyChanged(p))

    member this.RaisePropertyChanged(propertyName : string) =
        propertyChanged.Trigger(this, new PropertyChangedEventArgs(propertyName))
        
    member this.RaisePropertyChanged(expr : Expr) =
        let propName = getPropertyNameFromExpression(expr)
        this.RaisePropertyChanged(propName)

    /// Value used to notify view that an asynchronous operation is executing
    member this.OperationExecuting with get() = operationExecuting.Value and set(value) = operationExecuting.Value <- value

    /// Handles management of dependencies for all computed properties 
    /// as well as ICommand dependencies
    member this.DependencyTracker = depTracker :> IDependencyTracker

    /// Manages tracking of validation information for the entity
    member this.ValidationTracker = errorTracker :> IValidationTracker

    member this.IsValid with get() = not errorTracker.HasErrors

    member this.EntityErrors with get() = errorTracker.EntityErrors        
    member this.PropertyErrors with get() = errorTracker.PropertyErrors        

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member this.PropertyChanged = propertyChanged.Publish

    interface IRaisePropertyChanged with
        member this.RaisePropertyChanged(propertyName: string) =
            this.RaisePropertyChanged(propertyName)

    interface INotifyDataErrorInfo with
        member this.GetErrors propertyName = 
            errorTracker.GetErrors(propertyName) :> System.Collections.IEnumerable

        member this.HasErrors with get() = errorTracker.HasErrors

        [<CLIEvent>]
        member this.ErrorsChanged = errorsChanged.Publish
    
    interface IViewModel with
        member this.OperationExecuting with get() = this.OperationExecuting and set(v) = this.OperationExecuting <- v

        member this.DependencyTracker = this.DependencyTracker

    interface IViewModelPropertyFactory with
        member this.Backing (prop : Expr, defaultValue : 'a, validate : ValidationResult<'a> -> ValidationResult<'a>) =
            let validateFun = Validators.validate(getPropertyNameFromExpression(prop)) >> validate >> result
            NotifyingValueBackingField<'a>(getPropertyNameFromExpression(prop), propChanged, defaultValue, validationTracker, validateFun) :> INotifyingValue<'a>

        member this.Backing (prop : Expr, defaultValue : 'a, ?validate : 'a -> string list) =
            let validateFun = defaultArg validate (fun _ -> List.empty)
            NotifyingValueBackingField<'a>(getPropertyNameFromExpression(prop), propChanged, defaultValue, validationTracker, validateFun) :> INotifyingValue<'a>

        member this.FromFuncs (prop : Expr, getter, setter) =
            NotifyingValueFuncs<'a>(getPropertyNameFromExpression(prop), propChanged, getter, setter) :> INotifyingValue<'a>

        member this.CommandAsync(asyncWorkflow, ?token, ?onCancel) =
            let ct = defaultArg token CancellationToken.None
            let oc = defaultArg onCancel (fun e -> ())
            let cmd = Commands.createAsyncInternal asyncWorkflow getExecuting setExecuting (fun () -> true) ct oc
            cmd

        member this.CommandAsyncChecked(asyncWorkflow, canExecute, ?dependentProperties: Expr list, ?token, ?onCancel) =
            let ct = defaultArg token CancellationToken.None
            let oc = defaultArg onCancel (fun e -> ())
            let cmd = Commands.createAsyncInternal asyncWorkflow getExecuting setExecuting canExecute ct oc
            addCommandDependencies cmd dependentProperties
            cmd

        member this.CommandAsyncParam(asyncWorkflow, ?token, ?onCancel) =
            let ct = defaultArg token CancellationToken.None
            let oc = defaultArg onCancel (fun e -> ())
            let cmd = Commands.createAsyncParamInternal asyncWorkflow getExecuting setExecuting (fun _ -> true) ct oc
            cmd

        member this.CommandAsyncParamChecked(asyncWorkflow, canExecute, ?dependentProperties: Expr list, ?token, ?onCancel) =
            let ct = defaultArg token CancellationToken.None
            let oc = defaultArg onCancel (fun e -> ())
            let cmd = Commands.createAsyncParamInternal asyncWorkflow getExecuting setExecuting canExecute ct oc
            addCommandDependencies cmd dependentProperties
            cmd

        member this.CommandSync(execute) =
            let cmd = Commands.createSyncInternal execute (fun () -> true)
            cmd

        member this.CommandSyncChecked(execute, canExecute, ?dependentProperties: Expr list) =
            let cmd = Commands.createSyncInternal execute canExecute
            addCommandDependencies cmd dependentProperties
            cmd

        member this.CommandSyncParam(execute) =
            let cmd = Commands.createSyncParamInternal execute (fun _ -> true)
            cmd

        member this.CommandSyncParamChecked(execute, canExecute, ?dependentProperties: Expr list) =
            let cmd = Commands.createSyncParamInternal execute canExecute
            addCommandDependencies cmd dependentProperties
            cmd

