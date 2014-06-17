﻿(*
Copyright (c) 2013-2014 FSharp.ViewModule Team

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

open Microsoft.FSharp.Quotations
open System.Windows.Input
open System.ComponentModel.DataAnnotations
open System.ComponentModel

/// This is the specification required to determine which platform target the type provider builds
/// For example, this would specify a standard .NET assembly vs. PCL Profile7, etc
// TODO: Note that this likely would make more sense as a discriminated union + string - mostly just here as a placeholder atm
type Platform = { Framework : string }

/// The Specification used by the type provider to generate a view model
/// This can be implemented to allow use of any ViewModel and Command
/// Framework with the type provider
type IViewModuleTypeSpecification =

    /// The type used for the View Model.  Should be an open generic (typedefof<T>) implementing IViewModel<'a>
    abstract ViewModelType : System.Type
    
    /// The type used for implementing ICommand.  Should implement INotifyCommand
    abstract CommandType : System.Type
    
    /// The type provider target platform
    abstract Platform : Platform