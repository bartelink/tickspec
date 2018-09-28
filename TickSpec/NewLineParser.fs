﻿module internal TickSpec.NewLineParser

open System.Text.RegularExpressions

type internal ItemType =
    /// A bullet point
    | BulletPoint of string
    /// A row in table
    | TableRow of string list
    /// A multi-line string
    | MultiLineStringStart of int
    | MultiLineString of string
    | MultiLineStringEnd

/// Line type
type internal LineType =
    /// A start of file
    | FileStart
    /// A start of feature
    | FeatureName of string
    /// A feature description
    | FeatureDescription of string
    /// A start of background section
    | Background
    /// A start of scenario
    | Scenario of string
    /// A start of shared examples
    | SharedExamples
    /// A start of examples
    | Examples
    /// A step
    | Step of StepType
    /// An item
    | Item of LineType * ItemType
    /// A tag line
    | TagLine of string list

/// Try single parameter regular expression
let tryRegex input pattern =
    let m = Regex.Match(input, pattern, RegexOptions.IgnoreCase)
    if m.Success then m.Groups.[1].Value |> Some
    else None

let startsWith (pattern:string) (s:string) =
    s.StartsWith(pattern, System.StringComparison.InvariantCultureIgnoreCase)

let (|FeatureLine|_|) s =
    tryRegex s "^\s*Feature:\s(.*)"
    |> Option.map (fun t -> FeatureLine (t.Trim()))
let (|ScenarioLine|_|) s =
    let matches =
        [
            "^\s*Scenario:\s(.*)"
            "^\s*Example:\s(.*)"
            "^\s*Scenario Outline:\s(.*)"
            "^\s*Scenario Template:\s(.*)"
        ]
        |> Seq.map (tryRegex s)
        |> Seq.map (Option.map (fun t -> ScenarioLine (t.Trim())))
        |> Seq.choose id
    if matches |> Seq.isEmpty then None
    else matches |> Seq.head |> Some
let (|BackgroundLine|_|) s =
    tryRegex s "^\s*Background(.*)"
    |> Option.map (fun t -> BackgroundLine)
let (|GivenLine|_|) s =
    tryRegex s "^\s*Given\s(.*)"
    |> Option.map (fun t -> GivenLine (t.Trim()))
let (|WhenLine|_|) s =
    tryRegex s "^\s*When\s(.*)"
    |> Option.map (fun t -> WhenLine (t.Trim()))
let (|ThenLine|_|) s =
    tryRegex s "^\s*Then\s(.*)"
    |> Option.map (fun t -> ThenLine (t.Trim()))
let (|AndLine|_|) s =
    tryRegex s "^\s*And\s(.*)"
    |> Option.map (fun t -> AndLine (t.Trim()))
let (|ButLine|_|) s =
    tryRegex s "^\s*But\s(.*)"
    |> Option.map (fun t -> ButLine (t.Trim()))
let (|TableRowLine|_|) (s:string) =
    if s.Trim().StartsWith("|") then
        let options = System.StringSplitOptions.RemoveEmptyEntries
        let cols = s.Trim().Split([|'|'|],options)
        let cols = cols |> Array.toList |> List.map (fun s -> s.Trim())
        TableRowLine cols |> Some
    else None
let (|Bullet|_|) (s:string) =
    if s.Trim().StartsWith("*") then
        s.Substring(s.IndexOf("*")+1).Trim() |> Some
    else None
let (|DocMarker|_|) (s:string) =
    if s.Trim() = "\"\"\"" then Some (s.IndexOf('\"'))
    else None
let (|SharedExamplesLine|_|) (s:string) =
    if s.Trim() |> startsWith("Shared Examples") then Some SharedExamplesLine else None
let (|ExamplesLine|_|) (s:string) =
    if s.Trim() |> startsWith("Examples") then Some ExamplesLine else None
let (|Attributes|_|) (s:string) =
    if s.Trim().StartsWith("@") then
        let tags =
            seq { for tag in Regex.Matches(s,@"@(\w+)") do yield tag.Value.Substring(1) }
        Attributes (tags |> Seq.toList) |> Some
    else None

/// Line state given previous line state and new line text
let parseLine = function
    | FileStart, FeatureLine text
    | TagLine _, FeatureLine text
        -> FeatureName text |> Some
    | _, Attributes tags
        -> TagLine tags |> Some
    | FeatureName _, BackgroundLine
    | FeatureDescription _, BackgroundLine
        -> Background |> Some
    | FeatureName _, ScenarioLine text
    | FeatureDescription _, ScenarioLine text
    | Item(Step(_), TableRow _), ScenarioLine text
    | Item(Examples, TableRow _), ScenarioLine text
    | Item(_, BulletPoint _), ScenarioLine text
    | Step(_), ScenarioLine text
    | TagLine _, ScenarioLine text
    | Item(_, MultiLineStringEnd), ScenarioLine text
        -> Scenario text |> Some
    | Background, GivenLine text
    | Scenario _, GivenLine text
    | Step(_), GivenLine text
    | Step(GivenStep _), AndLine text
    | Step(GivenStep _), ButLine text
    | Item(Step(_), TableRow _), GivenLine text
    | Item(_, BulletPoint _), GivenLine text
    | Item(_, MultiLineStringEnd), GivenLine text
        -> Step(GivenStep text) |> Some
    | Background, WhenLine text
    | Scenario _, WhenLine text
    | Step(_), WhenLine text
    | Step(WhenStep _), AndLine text
    | Step(WhenStep _), ButLine text
    | Item(Step(_), TableRow _), WhenLine text
    | Item(_, BulletPoint _), WhenLine text
    | Item(_, MultiLineStringEnd), WhenLine text
        -> Step(WhenStep text) |> Some
    | Background, ThenLine text
    | Scenario _, ThenLine text
    | Step(_), ThenLine text
    | Step(ThenStep _), AndLine text
    | Step(ThenStep _), ButLine text
    | Item(Step(_), TableRow _), ThenLine text
    | Item(_, BulletPoint _), ThenLine text
    | Item(_, MultiLineStringEnd), ThenLine text
        -> Step(ThenStep text) |> Some
    | (Step(_) as l), TableRowLine cells
    | Item(l, TableRow _), TableRowLine cells
    | (Examples as l), TableRowLine cells
    | (SharedExamples as l), TableRowLine cells
        -> Item(l, TableRow cells) |> Some
    | (Step(_) as l), Bullet text
    | Item(l, BulletPoint _), Bullet text
        -> Item(l, BulletPoint text) |> Some
    | (Step(_) as l), DocMarker offset
        -> Item(l, MultiLineStringStart offset) |> Some
    | Item(l, MultiLineStringStart _), DocMarker _
    | Item(l, MultiLineString _), DocMarker _
        -> Item(l, MultiLineStringEnd) |> Some
    | Item(l, MultiLineStringStart _), line
    | Item(l, MultiLineString _), line
        -> Item(l, MultiLineString line) |> Some
    | Step(_), ExamplesLine
    | Item(Examples, TableRow _), ExamplesLine
    | Item(Step(_), TableRow _), ExamplesLine
    | Item(_, BulletPoint _), ExamplesLine
    | Item(_, MultiLineStringEnd), ExamplesLine
    | TagLine _, ExamplesLine
        -> Examples |> Some
    | Step(_), SharedExamplesLine
    | Item(_, TableRow _), SharedExamplesLine
    | Item(_, BulletPoint _), SharedExamplesLine
    | Item(_, MultiLineStringEnd), SharedExamplesLine
    | TagLine _, SharedExamplesLine
        -> SharedExamples |> Some
    | FeatureName _, line -> FeatureDescription line |> Some
    | _, _ -> None

let expectingLine = function
    | FileStart -> "Expecting feature definition in the beginning of file"
    | Scenario _ | Background -> "Expecting steps"
    | Examples | SharedExamples -> "Expecting table row"
    | Step(_) -> "Expecting another step, table row, bullet, examples or end of scenario"
    | Item(Step(_), TableRow _) -> "Expecting another table row, next step, examples or end of scenario"
    | Item(SharedExamples, _) -> "Expecting a table row"
    | Item(Examples, _) -> "Expecting a table row, another examples or end of scenario"
    | _ -> "Unexpected line"