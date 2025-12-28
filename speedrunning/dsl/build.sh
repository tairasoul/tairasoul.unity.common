antlr4 -Xexact-output-dir grammar/SrDslLexer.g4 -Dlanguage=CSharp -o antlr4-impl
antlr4 -Xexact-output-dir -lib antlr4-impl grammar/SrDslParser.g4 -Dlanguage=CSharp -visitor -o antlr4-impl