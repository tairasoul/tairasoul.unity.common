# tairasoul.unity.common

a bunch of common code for our unity mods (although attempts are made to make them work outside of unity)

datastreams : stream-derived classes

embedded : code that interacts with embedded resources

events : code for things like an event bus

hashing : implementations/ports of hashing algorithms

format : byte-based format relying on unsafe code, primarily for the networking layer

networking : a basic P2P-oriented networking layer

shared_projects : a bunch of .projitems for different common code, .csproj items for ones that also need a dependency and a .csproj with general csproj-related utils

sourcegen : source generators

speedrunning : DSL specifically for integrating LiveSplit with a unity game

util : general common util