﻿namespace FSharp.ViewModule.Core

open System.ComponentModel
open System.Windows.Input

/// <summary>Extension of ICommand with a public method to fire the CanExecuteChanged event</summary>
/// <remarks>This type should provide a constructor which accepts an Execute (obj -> unit) and CanExecute (obj -> bool) function</remarks>
type INotifyCommand =
    inherit ICommand 
    
    /// Trigger the CanExecuteChanged event for this specific ICommand
    abstract RaiseCanExecuteChanged : unit -> unit

/// Interface used to explicitly raise PropertyChanged
type IRaisePropertyChanged =
    inherit INotifyPropertyChanged

    abstract RaisePropertyChanged : string -> unit

/// Results of a validation for the member of a type
type ValidationResult = { MemberName : string ; Errors : string list }


/// <summary>Extension of INotifyPropertyChanged with a public method to fire the PropertyChanged event</summary>
/// <remarks>This type should provide a constructor which accepts no arguments, and one which accepts a Model</remarks>
type IViewModel =
    inherit INotifyPropertyChanged
    inherit IRaisePropertyChanged
    inherit INotifyDataErrorInfo
    
    /// Value used to notify view that an asynchronous operation is executing
    abstract OperationExecuting : bool with get, set

    /// Setup all errors for validation
    abstract SetErrors : ValidationResult seq -> unit

    /// Adds a permanent handler on PropertyChanged.
    /// If the given property name is the one that was changed, then notify the list of computed names.
    abstract AddNotifyComputeds : string * string list -> unit
