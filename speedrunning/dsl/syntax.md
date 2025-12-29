# SRD Syntax

syntax reference for the speedrunning dsl.

things of note:

keyword `fulfilled` - tells the compiler that this is where it should consider the split completed

object paths - these start and end with \\, like `\path/to/object\`. / is used to traverse the hierarchy of objects, if an object name has / in it, it should be escaped like `\/`

## File structure

files are structured as follows

```
order
	splitname
	splitname2

defsplit splitname
	condition
```

order has to come first in the file, then split definitions.

## Split definitions

split definitions start with a condition, then logic to run.

a split follows the structure of

```
defsplit splitname
	condition
		logic
```

logic may or may not be optional depending on the condition.

conditions with optional logic have `optional logic` immediately below their header.

## Grouping conditions

there are two grouping conditions, `any` and `all`.

they follow the structure of

```
defsplit splitname
	any | all
		condition
	do
		logic
```

conditions grouped in any or all should not use timer operations.

an `any` group will be considered complete when any of the operations within the group resolve to true.

an `all` group will be considered complete when all of the operations within the group resolve to true.

neither of these can be recursively placed within eachother (yet, might look into it in the future)

## Split conditions

there are 4 unique condition types, and 8 variants total.

logic is optional for some, and required for others.

### Bounds condition

optional logic

the bounds condition triggers based on player position.

the basic variant is as follows

`bounds [x, y, z] [x, y, z] groupedBoundType`

first xyz set is corner 1, second is corner 2 of the box.

groupedBoundType can be `never`, `once`, `active` or `inactive`.

`never` means it's considered complete if the player has never entered that area, `once` means it's considered complete if the player has ever entered that area, `active` means it's considered complete if the player is currently in that area and `inactive` means it's considered complete if the player is not currently in that area.

this has two variants

#### Bounds bound to an object

optional logic

a bounds condition can also be bound to an object's position and size

`bounds \path/to/object\ groupedBoundType`

this bound will then have it's center and size set to the same as the targetted object.

#### Bounds bound to the position of an object

optional logic

a bounds condition can also be bound only to an object's position, with a predefined size

`bounds \path/to/object\ [x, y, z] groupedBoundType`

this bound will have it's center set to the object's position, and the size set to the xyz set.

### Event listen


optional logic
the event listen condition is triggered when a specific event is sent over the event bus.

the first variant is as follows

`on EventName [x, y, z]`

`[x, y, z]` is the flat array passed to the event as defined by the runtime.

#### Event listen shorthand

optional logic

there's also a shorthand for event listening, which will be considered fulfilled if the event is ever sent once this split is active.

`on EventName`

this would be the same as writing

```
on EventName
	fulfilled
```

### Object condition

logic required

the object condition allows you to check an object (or a component's) properties.

the first variant is as follows

`condition \path/to/object\ [.property = varname]`

this condition will have issues if multiple objects have the same path.

`[.property = varname]` assigns /path/to/object.property to varname, using it in the following logic block will reference the value of .property

you can assign multiple, like `[.property = var1, .property2 = var2]`

or assign a property of a property, like `[.transform.position.x = var1, .transform.position.y = var2]`

#### Object component condition

logic required

the second variant of the object condition accesses the properties of a component

it is as follows

`condition \path/to/object\ .FullName.Of.Component [.property = varname]`

`.Fullname.Of.Component` is the full name of the component, as it would appear in UnityExplorer

so a Rigidbody would be `.UnityEngine.Rigidbody`

## Logic nodes

there are 3 logic nodes, and 1 shorthand.

### Timer node

a timer node is a command sent to Livesplit to control splits.

used like `timer operation`

operation can be any of the following:

start, split, startorsplit, skipsplit, pause, resume, unsplit, pausegametime, unpausegametime

### If statement

an if statement works the same as any other language

```if x >= 5 and y <= 500
	timer pause
else if x <= 900 and y >= 5
	timer split
else
	timer skipsplit
end
```

you can only join comparisons with `or`, `and` is not yet supported

supported comparisons are `>=`, `>`, `==`, `<=` and `<`

math operations are also supported, with the basic operators `+`, `-`, `/` (divide) and `*` (multiply), like `x + 8`

### Call nodes

call nodes can call runtime-provided methods with any primitive the dsl has access to (or a variable reference)

this has a shorthand, being just `MethodName [args]` instead of `call MethodName [args]`

for example, `call ChangeHudDisplay ["Now at split 5, player health {0}", hp]` will call ChangeHudDisplay("Now at split 5, player health {0}", hp) from the runtime-provided methods, taking `hp` from the variables in the current scope.

these have no return value

## RunImmediate node

a runimmediate condition isn't actually a condition, and just runs your logic the moment the split is hit.

using `fulfilled` in these is not required, as the compiler assumes these nodes are always complete

for example,

```
defsplit immediate
	runimmediate
		call ImmediateCall ["hello!"]
```

this split will, when reached, call ImmediateCall("hello!") and then move onto the next split.

## Example

a basic example used during testing of the language.

```
order
	split1
	split2
 
defsplit split1
 	any
 		on ItemPickup
		bounds [500, 20, 100] [800, 500, 100] active
 	do
		timer split
		call TestMethod ["hi"]
		TestMethod2 ["hello"]
		fulfilled

defsplit split2
	bounds [8, 5, 4] [7, 6, 2] once
		timer split
		fulfilled
```