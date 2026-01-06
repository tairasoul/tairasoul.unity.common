parser grammar SrDslParser;

options {
	tokenVocab = SrDslLexer;
}

root
	: split_order (splits +=splitdef_node)* EOF
	;

split_order
	: ORDER (order_def+=IDENTIFIER+)
	;

splitdef_node
	: SPLITDEF IDENTIFIER
	(any_node
		| all_node
		| nongrouped_condition_node
		| runimmediate_node)
	;

runimmediate_node
	: RUNIMMEDIATE (logic+=split_logic_node)+
	;

any_node
	: ANY (conditions+=grouped_condition_node)+ DO (logic+=split_logic_node)+
	;

all_node
	: ALL (conditions+=grouped_condition_node)+ DO (logic+=split_logic_node)+
	;

gcn_opt
	: bounds_grouped
	| event_listen_group
	| bounds_object_grouped
	| bounds_object_size_grouped
	;

gcn_nonopt
	: object_condition
	| object_condition_component
	| event_listen
	;

grouped_condition_node_opt
	: gcn_opt (logic+=split_logic_node)*
	;

grouped_condition_node_nonopt
	: gcn_nonopt (logic+=split_logic_node)+
	;

grouped_condition_node
	:	grouped_condition_node_opt
	| grouped_condition_node_nonopt
	;

condition_node_opt
	:	bounds_grouped
	| event_listen_group
	| bounds_object_grouped
	| bounds_object_size_grouped
	;

condition_node_nonopt
	: object_condition
	| object_condition_component
	| event_listen
	;

nongrouped_condition_node_opt
	: condition_node_opt (logic+=split_logic_node)*
	;

nongrouped_condition_node_nonopt
	: condition_node_nonopt (logic+=split_logic_node)+
	;

nongrouped_condition_node
	: nongrouped_condition_node_opt
	| nongrouped_condition_node_nonopt
	;

split_logic_node
	: logic_node
	| FULFILLED
	;

logic_node
	: timer_node
	| if_statement
	| call_node
	| call_shorthand
	;

call_arg
	: IDENTIFIER
	| datatype
	;

call_node
	: CALL IDENTIFIER ('[' call_args+=call_arg (',' call_args+=call_arg)* ']')?
	;

call_shorthand
	: IDENTIFIER ('[' call_args+=call_arg (',' call_args+=call_arg)* ']')
	;

comparison_binaryrh_node
	: IDENTIFIER
	| datatype
	| binary
	;

binary_lh_node
	: IDENTIFIER
	| datatype
	;

comparison_full
	: <lh, op, rh> lh=comparison_binaryrh_node op=COMPARISON rh=comparison_binaryrh_node
	;

comparison_shorthand
	: comparison_binaryrh_node
	;

comparison_l
	: comparison_full
	| comparison_shorthand
	;

comparison_andor
	: <andor> andor=AND comparison_l
	;

comparison
	: <main_comparison> main_comparison=comparison_l (extra_comparisons+=comparison_andor)*
	;

binary
	: <lh, op, rh> lh=binary_lh_node op=MATH_OP rh=comparison_binaryrh_node
	;

if_branch
	: else_statement
	| else_if_statement
	;

if_statement
	: <comp, branch> IF comp=comparison (logic+=split_logic_node)+
	(branch=if_branch)? END
	;

else_statement
	: ELSE (logic+=split_logic_node)+
	;

else_if_statement
	: <comp, branch> ELSE IF comp=comparison (logic+=split_logic_node)+
	(branch=if_branch)?
	;

timer_node
	: TIMER TIMER_ACTION
	;

event_listen_group
	: <event_name> ON event_name=IDENTIFIER ANYPOINT?
	;

event_listen
	: <event_name> ON event_name=IDENTIFIER ('[' args+=IDENTIFIER (',' args+=IDENTIFIER)* ']') ANYPOINT?
	;

bounds_grouped
	:	bounds_condition GROUPED_BOUND_TYPE
	;

bounds_object_grouped
	:	bounds_object GROUPED_BOUND_TYPE
	;

bounds_object_size_grouped
	:	bounds_object_size GROUPED_BOUND_TYPE
	;

bounds_condition
	: BOUNDS '[' numbers+=number ',' numbers+=number ',' numbers+=number ']' '[' numbers+=number ',' numbers+=number ',' numbers+=number ']'
	;

bounds_object
	: <path> BOUNDS path=PATH
	;

bounds_object_size
	:	<path> BOUNDS path=PATH '[' numbers+=number ',' numbers+=number ',' numbers+=number ']'
	;

property_access
	: DOT_ACCESSED '=' IDENTIFIER
	;

object_condition
	: <path, component> CONDITION path=PATH ('[' args+=property_access (',' args+=property_access)* ']') 
	;

object_condition_component
	: <path, component, property> CONDITION path=PATH component=DOT_ACCESSED ('[' args+=property_access (',' args+=property_access)* ']') 
	;

datatype
	: STRING
	| number
	| bool_dt
	;

number 
	: FLOAT
	| INTEGER
	;

bool_dt
	: TRUE
	| FALSE
	;