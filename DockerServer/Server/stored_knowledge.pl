% Stored knowledge always belongs to an observer. Both lifetimes participate;
% mutable values have already been replaced by the Python storage layer.
remembered_fact(Type, Subject, Target, Distance, Actor) :-
    stored_fact(Actor, permanent, Type, Subject, Target, Distance).

remembered_fact(Type, Subject, Target, Distance, Actor) :-
    stored_fact(Actor, mutable, Type, Subject, Target, Distance).

remembered_lever_activation(Lever, Actor) :-
    remembered_fact(lever_activated, Lever, _, _, Actor).

remembered_attack(Victim, Attacker, Actor) :-
    remembered_fact(agent_attacked, Victim, Attacker, _, Actor).

remembered_zombie(Zombie, Actor) :-
    remembered_fact(zombie_seen, Zombie, _, _, Actor).

% Historical distances are deliberately NOT promoted to distance/3 or
% visibility. Remembering a sighting/attack does not prove present danger,
% and a remembered attack must not subtract health a second time.
