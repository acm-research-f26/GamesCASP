% Reasoned innocent policy: defaults, exceptions, and explicit priorities.
%
% Drop-in for innocent_behavior.pl. It keeps the same interface
%     action(Action, Actor) :- candidate_action(Action, Actor), not defeated(Action, Actor).
% so rule sets that add candidate_action/defeated clauses (for example the
% half-zombie rules) can extend it. innocent_behavior.pl stays unchanged as the
% "baseline" policy for like-for-like comparison with the Behavior Tree.
%
% Each candidate carries a priority kind. An action is defeated when another
% candidate for the same actor has a kind above it. Priorities are a ground
% precedence table, not numbers: with Ciao's constructive negation, proving
% "not outranked" must not quantify over a variable bound by a CLP comparison.
% Exceptions are
% written as default negation ("unless"), and explicit evidence uses strong
% negation: -reachable(L, A) means navigation PROVED no path, while missing
% information leaves the default (assume reachable) in place.
%
% Priority kinds, highest first:
%   flee      run_from       survive an immediate threat
%   finish    pull_lever     finish a lever the actor is already touching
%   defend    *_barricade    a barricade a visible zombie is breaking through
%   repair    *_barricade    a failing barricade nearby, or one the actor is already holding
%   pursue    move_to_lever  the preferred reachable lever
%   search    hunt_lever     levers remain and none is known
%   maintain  *_barricade    damaged barricades once every lever is pulled
%   idle      wait           always possible while alive, so a decision always exists

% ---------- Agent state ----------

can_act(Actor) :-
    health(Health, Actor),
    Health #> 0.

dead(Actor) :-
    health(Health, Actor),
    Health #=< 0.

levers_remaining :-
    levers_to_win(Required),
    levers_pulled_count(Current),
    Current #< Required.

final_lever :-
    levers_to_win(Required),
    levers_pulled_count(Current),
    Current #= Required - 1.

closer(Target1, Target2, Actor) :-
    distance(Target1, Distance1, Actor),
    distance(Target2, Distance2, Actor),
    Distance1 #< Distance2.

% ---------- Threats ----------

% Default: a visible zombie within 2 units is an immediate threat.
threat(Zombie, Actor) :-
    zombie(Zombie),
    distance(Zombie, Distance, Actor),
    Distance #=< 2.

% Within the zombies' attack range.
in_contact(Zombie, Actor) :-
    zombie(Zombie),
    distance(Zombie, Distance, Actor),
    Distance #=< 1.

in_contact_any(Actor) :-
    in_contact(_, Actor).

closer_threat(Zombie, Actor) :-
    threat(Zombie, Actor),
    threat(Other, Actor),
    Other \= Zombie,
    closer(Other, Zombie, Actor).

nearest_threat(Zombie, Actor) :-
    threat(Zombie, Actor),
    not closer_threat(Zombie, Actor).

% Exception to fleeing: the actor is touching the last lever needed to win and no
% zombie is close enough to strike before the pull completes.
ab_flee(Actor) :-
    finishing_final_lever(Actor).

finishing_final_lever(Actor) :-
    final_lever,
    touching_lever(Actor),
    not in_contact_any(Actor).

% ---------- Levers ----------

lever_available(Lever, Actor) :-
    lever_visible(Lever, Actor),
    lever_state(Lever, unpulled).

% Default: an available lever is pursued unless navigation proved it unreachable.
reachable_lever(Lever, Actor) :-
    lever_available(Lever, Actor),
    not -reachable(Lever, Actor).

has_lever_target(Actor) :-
    reachable_lever(_, Actor).

at_lever(Lever, Actor) :-
    distance(Lever, Distance, Actor),
    Distance #=< 1.

lever_threatened(Lever) :-
    nearest_zombie_distance_to_lever(Lever, Distance),
    Distance #=< 5.

lever_safe(Lever) :-
    nearest_zombie_distance_to_lever(Lever, Distance),
    Distance #> 5.

% Safe beats threatened; among safe levers the closer wins; among threatened
% levers the one with the farther zombie wins (same order as the baseline).
better_lever(Lever1, Lever2, Actor) :-
    reachable_lever(Lever1, Actor),
    reachable_lever(Lever2, Actor),
    Lever1 \= Lever2,
    lever_safe(Lever1),
    lever_threatened(Lever2).

better_lever(Lever1, Lever2, Actor) :-
    reachable_lever(Lever1, Actor),
    reachable_lever(Lever2, Actor),
    Lever1 \= Lever2,
    lever_safe(Lever1),
    lever_safe(Lever2),
    closer(Lever1, Lever2, Actor).

better_lever(Lever1, Lever2, Actor) :-
    reachable_lever(Lever1, Actor),
    reachable_lever(Lever2, Actor),
    Lever1 \= Lever2,
    lever_threatened(Lever1),
    lever_threatened(Lever2),
    nearest_zombie_distance_to_lever(Lever1, ZombieDistance1),
    nearest_zombie_distance_to_lever(Lever2, ZombieDistance2),
    ZombieDistance1 #> ZombieDistance2.

beaten_lever(Lever, Actor) :-
    reachable_lever(Lever, Actor),
    better_lever(Better, Lever, Actor),
    Better \= Lever.

preferred_lever(Lever, Actor) :-
    reachable_lever(Lever, Actor),
    not beaten_lever(Lever, Actor).

% ---------- Barricades ----------

zombie_at_barricade(Zombie, Barricade) :-
    zombie(Zombie),
    zombie_distance_to_barricade(Barricade, Distance, Zombie),
    Distance #=< 1.

under_attack(Barricade, Actor) :-
    barricade_visible(Barricade, Actor),
    zombie_at_barricade(_, Barricade).

% Defense is impossible once the barricade is gone and a zombie is in the opening.
overrun(Barricade) :-
    barricade_reinforcement(Barricade, Reinforcement),
    Reinforcement #=< 0,
    zombie_at_barricade(_, Barricade).

failing(Barricade, Actor) :-
    barricade_visible(Barricade, Actor),
    barricade_reinforcement(Barricade, Reinforcement),
    Reinforcement #=< 40.

damaged(Barricade, Actor) :-
    barricade_visible(Barricade, Actor),
    barricade_reinforcement(Barricade, Reinforcement),
    Reinforcement #< 95.

near_barricade(Barricade, Actor) :-
    distance(Barricade, Distance, Actor),
    Distance #=< 3.

at_barricade(Barricade, Actor) :-
    distance(Barricade, Distance, Actor),
    Distance #=< 1.

% Commitment: an actor already repairing a barricade finishes the job
% (hysteresis: start below 40, stop at 95) instead of oscillating.
holding(Barricade, Actor) :-
    current_action(hold_barricade(Barricade), Actor).

defend_goal(Barricade, Actor) :-
    under_attack(Barricade, Actor),
    not overrun(Barricade).

% Exception for every barricade goal: an overrun barricade cannot be held.
repair_goal(Barricade, Actor) :-
    failing(Barricade, Actor),
    near_barricade(Barricade, Actor),
    not overrun(Barricade),
    not defend_goal(Barricade, Actor).

repair_goal(Barricade, Actor) :-
    holding(Barricade, Actor),
    damaged(Barricade, Actor),
    not overrun(Barricade),
    not defend_goal(Barricade, Actor).

maintain_goal(Barricade, Actor) :-
    damaged(Barricade, Actor),
    not levers_remaining,
    not overrun(Barricade),
    not defend_goal(Barricade, Actor),
    not repair_goal(Barricade, Actor).

goal_kind(Barricade, Actor, defend) :- defend_goal(Barricade, Actor).
goal_kind(Barricade, Actor, repair) :- repair_goal(Barricade, Actor).
goal_kind(Barricade, Actor, maintain) :- maintain_goal(Barricade, Actor).

% Most important goal, then weakest barricade, then closest.
better_barricade(Barricade1, Barricade2, Actor) :-
    goal_kind(Barricade1, Actor, Kind1),
    goal_kind(Barricade2, Actor, Kind2),
    Barricade1 \= Barricade2,
    above(Kind1, Kind2).

better_barricade(Barricade1, Barricade2, Actor) :-
    goal_kind(Barricade1, Actor, Kind),
    goal_kind(Barricade2, Actor, Kind),
    Barricade1 \= Barricade2,
    barricade_reinforcement(Barricade1, Reinforcement1),
    barricade_reinforcement(Barricade2, Reinforcement2),
    Reinforcement1 #< Reinforcement2.

better_barricade(Barricade1, Barricade2, Actor) :-
    goal_kind(Barricade1, Actor, Kind),
    goal_kind(Barricade2, Actor, Kind),
    Barricade1 \= Barricade2,
    barricade_reinforcement(Barricade1, Reinforcement),
    barricade_reinforcement(Barricade2, Reinforcement),
    closer(Barricade1, Barricade2, Actor).

beaten_barricade(Barricade, Actor) :-
    goal_kind(Barricade, Actor, _),
    better_barricade(Better, Barricade, Actor),
    Better \= Barricade.

preferred_barricade(Barricade, Actor) :-
    goal_kind(Barricade, Actor, _),
    not beaten_barricade(Barricade, Actor).

% ---------- Priorities ----------

% above(Higher, Lower): the complete, explicit precedence order.
above(flee, finish).   above(flee, defend).   above(flee, repair).   above(flee, pursue).
above(flee, search).   above(flee, maintain). above(flee, idle).
above(finish, defend). above(finish, repair). above(finish, pursue). above(finish, search).
above(finish, maintain). above(finish, idle).
above(defend, repair). above(defend, pursue). above(defend, search). above(defend, maintain).
above(defend, idle).
above(repair, pursue). above(repair, search). above(repair, maintain). above(repair, idle).
above(pursue, search). above(pursue, maintain). above(pursue, idle).
above(search, maintain). above(search, idle).
above(maintain, idle).

% ---------- Candidates ----------

candidate(run_from(Zombie), Actor, flee) :-
    can_act(Actor),
    nearest_threat(Zombie, Actor),
    not ab_flee(Actor).

% Touching a reachable lever: pull it, whatever lever would otherwise be preferred.
candidate(pull_lever(Lever), Actor, finish) :-
    can_act(Actor),
    reachable_lever(Lever, Actor),
    at_lever(Lever, Actor).

candidate(hold_barricade(Barricade), Actor, Kind) :-
    can_act(Actor),
    preferred_barricade(Barricade, Actor),
    goal_kind(Barricade, Actor, Kind),
    at_barricade(Barricade, Actor).

candidate(move_to_barricade(Barricade), Actor, Kind) :-
    can_act(Actor),
    preferred_barricade(Barricade, Actor),
    goal_kind(Barricade, Actor, Kind),
    not at_barricade(Barricade, Actor).

candidate(move_to_lever(Lever), Actor, pursue) :-
    can_act(Actor),
    preferred_lever(Lever, Actor),
    not at_lever(Lever, Actor).

candidate(hunt_lever, Actor, search) :-
    can_act(Actor),
    levers_remaining,
    not has_lever_target(Actor).

candidate(wait, Actor, idle) :-
    can_act(Actor).

candidate_action(Action, Actor) :-
    candidate(Action, Actor, _).

% A candidate is defeated when a higher kind is active. Activation is a cheap,
% preference-free condition: whenever active(K) holds, some candidate of kind K
% exists, or a candidate of a kind above K does (barricade kinds below the
% chosen goal; pursue while touching a lever). Either way, everything below K is
% correctly defeated. Proving "not active" is far cheaper than re-deriving the
% preferred lever and barricade inside negation, and it quantifies only over
% ground kinds (Ciao's dual over action terms of different shapes, and \= over
% compound terms, are not reliable).
outranked(Action, Actor) :-
    candidate(Action, Actor, Kind),
    higher_active(Kind, Actor).

higher_active(Kind, Actor) :-
    above(Higher, Kind),
    active(Higher, Actor).

threatened(Actor) :-
    threat(_, Actor).

touching_lever(Actor) :-
    reachable_lever(Lever, Actor),
    at_lever(Lever, Actor).

active(flee, Actor) :- can_act(Actor), threatened(Actor), not ab_flee(Actor).
active(finish, Actor) :- can_act(Actor), touching_lever(Actor).
active(defend, Actor) :- can_act(Actor), goal_kind(_, Actor, defend).
active(repair, Actor) :- can_act(Actor), goal_kind(_, Actor, repair).
active(pursue, Actor) :- can_act(Actor), has_lever_target(Actor).
active(search, Actor) :- can_act(Actor), levers_remaining, not has_lever_target(Actor).
active(maintain, Actor) :- can_act(Actor), goal_kind(_, Actor, maintain).

defeated(Action, Actor) :-
    outranked(Action, Actor).

% Python queries action(Action, Actor).
action(Action, Actor) :-
    candidate_action(Action, Actor),
    not defeated(Action, Actor).
