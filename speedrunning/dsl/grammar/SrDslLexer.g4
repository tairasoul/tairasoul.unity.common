lexer grammar SrDslLexer;

GROUPED_BOUND_TYPE
	: 'never'
	| 'once'
	| 'active'
	| 'inactive'
	;

TIMER_ACTION
	: 'pause'
	| 'resume'
	| 'start'
	| 'split'
	| 'startorsplit'
	| 'unsplit'
	| 'skipsplit'
	| 'pausegametime'
	| 'unpausegametime'
	;

COMPARISON
	: EQUAL2
	| RARROW
	| RARROWEQUAL
	| LARROW
	| LARROWEQUAL
	| NOTEQUAL
	;

MATH_OP
	:	PLUS
	| MINUS
	| MULTIPLY
	| DIVIDE
	;

CALL
	: 'call'
	;

CONDITION
	: 'condition'
	;

END
	: 'end'
	;

ANY
	: 'any'
	;

ALL
	: 'all'
	;

TIMER
	: 'timer'
	;

BOUNDS
	: 'bounds'
	;

SPLITDEF
	: 'defsplit'
	;

ORDER
	: 'order'
	;

IF
	: 'if'
	;

ELSE
	: 'else'
	;

ON
	: 'on'
	;

RUNIMMEDIATE
	: 'runimmediate'
	;

AND
	: 'and'
	;

// OR
// 	: 'or'
// 	;

FULFILLED
	: 'fulfilled'
	;

DO
	: 'do'
	;

QUOTE
	: '"'
	;

DOT_ACCESSED
	: '.' IDENTIFIER ('.' IDENTIFIER)*
	;

PATH
	: '\\' .+? '\\'
	;

NOTEQUAL
	: '!='
	;

EQUALS
	: '='
	;

EQUAL2
	: '=='
	;

LARROW
	: '<'
	;

LARROWEQUAL
	: '<='
	;

PLUS
	: '+'
	;

MINUS
	: '-'
	;

MULTIPLY
	: '*'
	;

STRING
	: '"' .*? '"'
	;

DIVIDE
	: '/'
	;

RARROW
	: '>'
	;

RARROWEQUAL
	: '>='
	;

COMMENT
	: '//' ~ [\n\r]* -> channel(HIDDEN)
	;

LBRACKET
	: '['
	;

RBRACKET
	: ']'
	;

COMMA
	: ','
	;

TRUE
	: 'true'
	;

FALSE
	: 'false'
	;

WHITESPACE
	: [ \t\r\n]+ -> skip
	;

FLOAT
	: MINUS? Digit+ '.' Digit+
	;

INTEGER
	: MINUS? Digit+
	;

IDENTIFIER
	: Letter+ LetterOrDigit*
	;

fragment LetterOrDigit
	: Letter
	| Digit
	;

fragment Letter
	: [a-zA-Z$_]
	;

fragment Digit
	: [0-9]
	;