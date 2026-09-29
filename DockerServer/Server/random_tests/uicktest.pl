% ============================================================
% toy_example.pl
% Demonstrates: actions of different arities coming out of the
% same chosen_action/1, no padding, no two-stage query.
% ============================================================

% ---------- facts for this run ----------
hungry.
message_from(alice).
message_from(bob).
weather(rainy).

% ---------- valid actions: note the mixed arities ----------
is_valid(make_coffee) :-
    not have_coffee.

is_valid(eat_breakfast) :-
    hungry.

is_valid(bring_umbrella) :-
    weather(rainy).

is_valid(reply_to(Person)) :-
    message_from(Person).

is_valid(wander_randomly).   % fallback, always available

% ---------- priorities ----------
priority(make_coffee, 5).
priority(eat_breakfast, 6).
priority(bring_umbrella, 4).
priority(reply_to(alice), 8).   % Alice's message matters more than Bob's
priority(reply_to(bob), 3).
priority(wander_randomly, 1).

% ---------- argmax, same pattern as the game ----------
chosen_action(Action) :-
    is_valid(Action),
    priority(Action, P).

exists_better_action(Action, P) :-
    is_valid(Other),
    Other \= Action,
    priority(Other, P2),
    P2 > P.

?- chosen_action(X).