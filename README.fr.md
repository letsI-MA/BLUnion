<p align="center"><img src="images/icon.png" width="160" alt="Logo BLUnion"></p>

# BLUnion

Un plugin Dalamud pour FINAL FANTASY XIV qui aide les groupes de mages bleus à décider ensemble quoi apprendre ensuite.

<p align="center">
  <a href="#installation"><b>Installer</b></a> ·
  <a href="#fonctionnalités">Fonctionnalités</a> ·
  <a href="#synchronisation">Synchro</a> ·
  <a href="#recherche-de-groupe">Recherche de groupe</a> ·
  <a href="#données-stockées">Confidentialité</a> ·
  <a href="#discord">Discord</a> ·
  <a href="#limites-connues">Limites connues</a>
</p>

<p align="center">
  <a href="README.md">English</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <b>Français</b> ·
  <a href="README.ja.md">日本語</a>
</p>

> Ceci est une traduction du [README anglais](README.md). Elle peut être en retard sur la version anglaise - en cas de doute, c'est la version anglaise qui fait foi.

Si tu as déjà essayé d'organiser une chasse aux sorts de mage bleu entre amis, tu connais la scène : quelqu'un demande « attends, qui a encore besoin de Diamondback ? », personne ne s'en souvient, et vous finissez par comparer vos grimoires à la main. BLUnion s'occupe de cette partie.

Le plugin lit directement dans le jeu les sorts que tu as appris (aucune saisie manuelle), compare ta progression avec celle de ton groupe et t'indique où obtenir les sorts qui te manquent - monstre, zone et coordonnées quand elles sont connues. L'onglet « Plan d'apprentissage » regroupe en plus les sorts manquants par monstre : si un ennemi enseigne trois sorts dont ton groupe a besoin, tu le vois d'un coup d'œil au lieu de chercher sort par sort.

Au départ, c'était un outil pour moi et quelques amis. Aujourd'hui d'autres joueurs l'utilisent aussi, ce qui me fait très plaisir, mais cela reste un projet perso que je développe sur mon temps libre. Il peut donc rester quelques petits défauts.

## Fonctionnalités

- Détecte ton groupe et les mages bleus qui en font partie
- Lit automatiquement l'état de tes propres sorts (via `IUnlockState` de Dalamud)
- Aperçu avec la progression moyenne de ton groupe et une « meilleure prochaine cible » - le sort qui aide le plus de monde à la fois
- Compare plusieurs joueurs et trie les sorts manquants selon le nombre de personnes qui en ont réellement besoin
- Grimoire avec les 124 sorts, filtrable par appris/manquants
- Sets de sorts pour les 32 étapes de La Grande Mascarade ainsi que des sets par rôle (Tank, Soigneur, Soigneur d'appoint, DPS), avec à chaque fois le nombre de sorts nécessaires que tu possèdes déjà
- Indique où obtenir chaque sort - monstre, zone ou mission, et coordonnées sur la carte pour la plupart des sources en monde ouvert
- Lieux cliquables : un nom de mission ouvre l'outil de mission avec cette mission sélectionnée (pour les entrées groupées comme « Thornmarch (Hard ou Extreme) », tu choisis d'abord la variante), une zone en monde ouvert te téléporte vers l'éthérite correspondante et place un drapeau sur ta carte. Les icônes de carte ouvrent la carte avec un drapeau ou écrivent le lien de la carte dans ta propre fenêtre de chat
- Filtre par nom ou par numéro de sort (`58`, `#058`, comme tu veux)
- Option pour masquer les sorts obtenus uniquement via les totems, si tu ne les cherches pas en ce moment
- Recherche de groupe pour trouver d'autres mages bleus sur ton data center
- Synchronisation par un court code d'export/import, ou en option par synchro en direct (Live-Sync) pour ne plus avoir à échanger de codes du tout
- Interface complète en allemand, anglais, français et japonais
- Une [version navigateur](https://letsi-ma.github.io/BLUnion/) pour ceux qui ne veulent pas du tout installer le plugin

### Ce qu'il fait en jeu (et ce qu'il ne fait pas)

BLUnion n'automatise pas le gameplay. Un clic dans le plugin ne déclenche jamais qu'une seule action, et uniquement quand tu cliques :

- **Les noms de missions** ouvrent la fenêtre de l'outil de mission avec la mission sélectionnée. L'inscription reste à faire toi-même.
- **Les zones en monde ouvert** te téléportent une seule fois vers l'éthérite la plus proche du monstre (ou l'éthérite principale de la zone), puis placent le drapeau sur la carte. Cela coûte des gils comme via la fenêtre de téléportation, c'est bloqué par le jeu partout où la téléportation est interdite (missions, combat, artisanat...) et ce n'est jamais réessayé. Tu n'en veux pas ? Désactive « Téléportation en cliquant sur un nom de lieu » dans les paramètres et les noms deviennent du simple texte.
- **Les icônes de carte** ouvrent la carte avec un drapeau ou écrivent le lien de la carte dans ta propre fenêtre de chat. Rien n'est envoyé à qui que ce soit.

## Synchronisation

Il existe deux méthodes, utilise celle que tu préfères.

Par défaut, ce sont des codes. Tu exportes ton statut sous forme d'un court code `BLU:...` et tu l'envoies comme tu veux - chat de groupe, Discord, peu importe. Par défaut, le plugin repère les codes qui apparaissent dans ton chat et les ajoute automatiquement à la comparaison ; dans un groupe, il suffit donc en général que chacun clique une fois sur Exporter. Tu peux désactiver ce comportement dans les paramètres. Aucun serveur n'intervient, tout reste sur ta machine.

Si tu actives la synchro en direct dans les paramètres, l'état de tes sorts est envoyé sur un petit serveur, et le plugin récupère tout seul l'état des autres mages bleus de ton groupe. Plus de codes, et les autres voient tes nouveaux sorts peu après que tu les as appris.

Sans le plugin Dalamud, la [version navigateur](https://letsi-ma.github.io/BLUnion/) fait la même chose dans ton navigateur - générer un code, lire celui de quelqu'un d'autre ou utiliser la recherche de groupe.

## Recherche de groupe

Repose sur la synchro en direct. Tu peux t'inscrire dans une liste publique pour ton data center, avec une courte note, tes horaires de jeu habituels (matin, soir, week-end...), le nombre de coéquipiers recherchés et les sorts que tu veux farmer. Les autres peuvent parcourir cette liste, la filtrer selon les sorts dont ils ont besoin et voir tout de suite ce qu'ils apporteraient à un groupe et ce qui leur manquerait encore. Un clic ajoute un joueur à ta comparaison.

Si tu choisis de partager ton statut en ligne, les autres verront dans la liste si tu es connecté en ce moment.

Pour l'instant, la recherche de groupe ne contient que des entrées de joueurs individuels - les annonces de groupe sont désactivées.

Rien n'est public tant que tu ne publies pas toi-même une entrée, et tu peux la supprimer à tout moment.

Les noms de personnages dans la recherche de groupe ne sont pas vérifiés - n'importe qui pourrait publier sous le nom de quelqu'un d'autre. Si tu repères une fausse entrée ou que quelqu'un utilise ton nom, signale-le sur le [Discord](https://discord.gg/uGW7kBG67) et elle sera supprimée.

## Données stockées

Uniquement si tu utilises la synchro en direct ou la recherche de groupe - sinon, rien ne quitte ton PC.

- Nom du personnage, monde et data center
- Les sorts que tu as appris (sous forme de masque de bits compact)
- Si tu publies une entrée dans la recherche de groupe : ta note, tes disponibilités, le nombre de coéquipiers souhaité et tes sorts cibles
- Un jeton de modification haché, pour que toi seul puisses modifier ou supprimer ton profil
- Uniquement si tu actives « Partager le statut en ligne » (désactivé par défaut) et que ton entrée est listée dans la recherche de groupe : si tu es en ligne et quand tu as été vu pour la dernière fois, arrondi à 15 minutes. Le signal provient uniquement de ton propre plugin (connexion, déconnexion et une fois par heure pendant que tu joues). Ces données sont supprimées avec ton profil, quand tu désactives l'option ou après 30 jours sans signal

À la première ouverture, le plugin te demande si tu veux utiliser la synchro en direct ou seulement les codes manuels ; tu peux changer ce choix (et le statut en ligne) à tout moment dans les paramètres.

Les profils expirent d'eux-mêmes après 90 jours sans mise à jour, et « Supprimer mon profil » dans les paramètres supprime le tien immédiatement. Les noms ne sont pas vérifiés auprès du jeu ; les abus (par ex. un profil à ton nom qui n'est pas le tien) peuvent être signalés sur le [Discord](https://discord.gg/uGW7kBG67). Le backend est un Cloudflare Worker, son code se trouve dans [`worker/`](worker/).

## Installation

Le plus simple est de passer par le dépôt de plugins personnalisé :

1. En jeu : `/xlsettings` → Experimental → Custom Plugin Repositories
2. Ajoute : `https://raw.githubusercontent.com/letsI-MA/BLUnion/main/pluginmaster.json`
3. Enregistre. BLUnion apparaît désormais dans les plugins disponibles de `/xlplugins`, mises à jour comprises.

Tu préfères compiler toi-même ? Clone le dépôt, lance `dotnet build`, puis indique la DLL générée dans les Dev Plugin Locations de Dalamud.

Dans les deux cas, `/blunion` ouvre la fenêtre.

## Origine des données

Les noms des sorts, des monstres et des lieux sont lus à l'exécution directement dans les fichiers du jeu, dans la langue que tu as choisie. Les détails sur les sources se trouvent dans le [README anglais](README.md#where-the-data-comes-from).

## Discord

Il existe un Discord communautaire : https://discord.gg/uGW7kBG67.

Chaque entrée publique de la recherche de groupe y reçoit automatiquement sa propre carte dans l'un des quatre salons régionaux (Amérique du Nord, Europe, Japon, Océanie). Il y a aussi deux commandes slash : `/blunion browse` affiche la liste directement dans Discord, et `/blunion online` montre qui est en ligne en ce moment sur un data center (uniquement les entrées qui partagent leur statut en ligne). Pour l'instant, Discord est en lecture seule - tu ne peux pas y créer d'entrées.

## Limites connues

- Les coordonnées des sources en monde ouvert sont maintenues à la main (elles ne peuvent pas être lues dans les fichiers du jeu), il en manque donc encore pour quelques lieux
- Une nouvelle entrée dans la recherche de groupe peut mettre jusqu'à environ une minute à apparaître pour les autres (c'est dû au stockage utilisé, ce n'est pas un bug)

## Licence

Voir [LICENSE](LICENSE).

# Unite. Learn. Mimic.

---

Projet de fan non officiel, sans lien avec SQUARE ENIX. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.
