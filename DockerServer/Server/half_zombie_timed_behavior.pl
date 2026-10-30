% Second draft: load this INSTEAD OF half_zombie_behavior.pl.
% Do not include both: the original flee defeaters would still block biting.
#include('innocent_behavior.pl').
#include('half_zombie_rules.pl').

% Snapshot inputs, in seconds on the same game-time clock:
% current_time(Now).
% bite_window_started_at(Start, Actor).
% Supply ONE current start per half zombie: initial infection or latest
% successful bite BEFORE expiry. Historical bit_human facts do not reset time.
bite_window_seconds(30).
critical_bite_seconds(15).

% Remaining time is useful to inspect. Stage predicates below compare the
% timestamps directly so negation does not have to constrain an intermediate
% Remaining variable (which caused fractional-boundary failures in Ciao).
bite_time_remaining(Remaining, Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    Now #>= Start,
    bite_window_seconds(Window),
    Remaining #= Start + Window - Now.

bite_window_active(Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    bite_window_seconds(Window),
    Now #>= Start,
    Now #< Start + Window.

critical_bite_stage(Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    bite_window_seconds(Window),
    critical_bite_seconds(Critical),
    Now #>= Start + Window - Critical,
    Now #< Start + Window.

bite_window_expired(Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    bite_window_seconds(Window),
    Now #>= Start + Window.

transformed_zombie(Actor) :-
    bite_window_expired(Actor).

zombie(Actor) :-
    transformed_zombie(Actor).

eliminated(Actor) :-
    transformed_zombie(Actor).

% half_zombie remains the input/history marker. This predicate describes the
% current playable status, including any elimination supplied by the caller.
active_half_zombie(Actor) :-
    half_zombie(Actor),
    bite_window_active(Actor),
    not eliminated(Actor).

candidate_action(attack_human(Target), Actor) :-
    can_act(Actor),
    active_half_zombie(Actor),
    preferred_human_target(Target, Actor),
    within_bite_range(Target, Actor).

candidate_action(move_to_human(Target), Actor) :-
    can_act(Actor),
    active_half_zombie(Actor),
    preferred_human_target(Target, Actor),
    not within_bite_range(Target, Actor).

candidate_action(wait, Actor) :-
    can_act(Actor),
    active_half_zombie(Actor),
    not innocent(Actor),
    not visible_human_target(Actor),
    not immediate_danger(Actor).

% Expiry/elimination blocks EVERY action, including inherited innocent actions.
defeated(_, Actor) :-
    eliminated(Actor).

% A half zombie needs a valid timer; a missing clock/start or future start must
% not silently grant an unlimited lifetime through inherited human actions.
defeated(_, Actor) :-
    half_zombie(Actor),
    not bite_window_active(Actor).

% In the final 15 seconds, only approach/attack survive. This also defeats
% run_from and any future human objectives without enumerating each one.
critical_bite_action(attack_human(_)).
critical_bite_action(move_to_human(_)).

defeated(Action, Actor) :-
    critical_bite_stage(Actor),
    not critical_bite_action(Action).

% The important exception: danger only defeats biting OUTSIDE the critical
% stage. An additional candidate rule cannot undo an existing defeated rule.
defeated(attack_human(_), Actor) :-
    immediate_danger(Actor),
    not critical_bite_stage(Actor).

defeated(move_to_human(_), Actor) :-
    immediate_danger(Actor),
    not critical_bite_stage(Actor).

% Outside the critical stage, preserve the first draft's human-hunting priority.
defeated(hold_barricade(_), Actor) :-
    visible_human_target(Actor).

defeated(move_to_barricade(_), Actor) :-
    visible_human_target(Actor).

defeated(pull_lever(_), Actor) :-
    visible_human_target(Actor).

defeated(move_to_lever(_), Actor) :-
    visible_human_target(Actor).

defeated(hunt_lever, Actor) :-
    visible_human_target(Actor).

defeated(wait, Actor) :-
    visible_human_target(Actor).
