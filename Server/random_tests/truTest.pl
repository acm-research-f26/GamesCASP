% ============================================================
% BASE PRIORITIES
% ============================================================

base_priority(attack, 50).
base_priority(flee, 40).
base_priority(wander, 10).


% ============================================================
% WORLD CONDITIONS
% ============================================================

enemy_nearby(player).
enemy_low_health(player).
low_health(player).


% ============================================================
% PRIORITY MODIFIERS
% ============================================================

modifier(Actor, attack, 10) :-
    enemy_nearby(Actor).

modifier(Actor, attack, 5) :-
    enemy_low_health(Actor).

modifier(Actor, attack, -20) :-
    low_health(Actor).

modifier(Actor, flee, 50) :-
    low_health(Actor).

modifier(Actor, wander, 10) :-
    not enemy_nearby(Actor).


% ============================================================
% SUM A LIST OF NUMBERS
% ============================================================

sum_list([], 0).

sum_list([H|T], Sum) :-
    sum_list(T, Rest),
    Sum is H + Rest.


% ============================================================
% CALCULATE FINAL PRIORITY
% ============================================================

priority(Actor, Action, Priority) :-
    base_priority(Action, Base),
    findall(
        Modifier,
        modifier(Actor, Action, Modifier),
        Modifiers
    ),
    sum_list(Modifiers, TotalModifier),
    Priority is Base + TotalModifier.


% ============================================================
% ARGMAX
% ============================================================

% Find the highest-scoring action in a nonempty list.

argmax([[Score, Action]|Rest], [BestScore, BestAction]) :-
    max_pair(Rest, RestBest),
    choose_max(
        [Score, Action],
        RestBest,
        [BestScore, BestAction]
    ).

% A single candidate is already the maximum.
max_pair([[Score, Action]], [Score, Action]).

% Recursively find the maximum of the remaining candidates.
max_pair([[Score, Action]|Rest], Best) :-
    max_pair(Rest, RestBest),
    choose_max([Score, Action], RestBest, Best).

% Keep the first candidate when scores tie.
choose_max([Score, Action], [OtherScore, _], [Score, Action]) :-
    Score >= OtherScore.

choose_max([Score, _], [OtherScore, OtherAction], [OtherScore, OtherAction]) :-
    Score < OtherScore.


% ============================================================
% SELECT THE BEST ACTION
% ============================================================

best_action(Actor, Action, Score) :-
    findall(
        [Priority, Candidate],
        priority(Actor, Candidate, Priority),
        ScoredActions
    ),
    argmax(ScoredActions, [Score, Action]).

% ============================================================
% QUERY
% ============================================================

?- best_action(player, Action, Score).