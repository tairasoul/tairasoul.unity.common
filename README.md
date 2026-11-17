# tairasoul.unity.common

a bunch of common code for our unity mods (although attempts are made to make them work outside of unity)

datastreams : stream-derived classes

embedded : code that interacts with embedded resources

events : event bus

hashing : implementations/ports of hashing algorithms

format : byte-based format relying on unsafe code, primarily for the networking layer (usually faster than binaryreader/writer)

networking : a basic P2P-oriented networking layer

repacker : cecil IL repacker library, currently missing repacking user-defined assembly and module attributes (tested by repacking repacker/test/test)

shared_projects : a bunch of .projitems for different common code, .csproj items for ones that also need a dependency and a .csproj with general csproj-related utils

sourcegen : source generators for projects in here

speedrunning : DSL specifically for integrating LiveSplit with a unity game (needs rewrite to parser combinator)

util : general common util

weavers: post-build weavers used via csproj_util's RunAfterBuild