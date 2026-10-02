# -*- coding: utf-8 -*-
"""The player guides shipped with the mod, in every language the game has.

Button names are written as {B:English text} and filled in from the mod's own translation
tables, so a guide always names the buttons exactly as the game shows them. {VERSION} is the
mod's version. Run  python tests/Generate-Guides.py  after editing.
"""

INSTALL = {}
WORKSHOP = {}

# ---------------------------------------------------------------- English
INSTALL['en'] = """Graveyard Keeper 2 Co-op {VERSION}

INSTALL
1. Everyone who plays together needs this mod, in the same version.
2. Close the game. Extract this archive into the game folder, next to
   GraveyardKeeper2.exe, keeping its folders.

PLAY OVER STEAM
3. Host: in the main menu press "{B:Co-op}", then "{B:Host a game}". Choose the
   number of players and press "{B:Host on Steam}". Load a save or start a new
   game. In the game, press F10 to invite friends.
4. Friends: accept the invite in Steam, or open "{B:Co-op}" > "{B:Join a game}"
   and press "{B:Join}" next to the host. You play in a copy of the host's world;
   your own saves are not touched.

PLAY WITHOUT STEAM
5. Host: "{B:Host a game}" > "{B:Host without Steam}" > "{B:Host}". Press
   "{B:Show my addresses}" and give one of them to the others.
6. Friends: "{B:Join a game}" > "{B:Join by address}", enter the address, press
   "{B:Join}". Over the internet the host's router has to forward UDP port 8889,
   or use a VPN such as Tailscale.

KEYS
T chat - F6 status - F7 name tags - F10 invite Steam friends - F11 change your look
Controller: the co-op menu works with it; in a game, Pause > "{B:Co-op}" has
what the F keys do.

Shared: the world, crafting, gardens, zombies, conveyors, vendors, reputation,
the weather, the tech tree and the gems. Each player keeps their own
inventory, money, energy, health, look, talents and position. The night passes
only when everyone sleeps.
The host can choose "{B:I sleep}" under "{B:Host a game}"; then the host's sleep
passes the night for everyone.

Story scenes and sermons: when a scene starts for one player, the others are asked whether
to watch it. Watching takes you there and back afterwards; "{B:Stop watching}"
ends it early. Keys: Y watch, N keep playing or stop; controller: Y and B
(hold B to stop). Missed one? Pause > "{B:Co-op}" lets you watch it from the
start while it runs.

FIGHTS
One player fights; one fight at a time per world. While someone fights, the
others are told who is fighting and how it ended, cannot start a fight of
their own, and the clock stops for everyone. The rewards drop at the
fighter's feet; the level's progress is shared.

NOT YET IN CO-OP
Fighting together; the answers another player picks in a conversation (their
lines show nearby); burial and autopsy work in progress (one player at a time
works a grave or table). Tested with two to four players.
Trade with a vendor one player at a time: two deals at the same moment can
both get its last item.

UNINSTALL
Delete BepInEx\\plugins\\GK2Coop in the game folder. To remove BepInEx as
well (if no other mod uses it), also delete the BepInEx folder, winhttp.dll,
doorstop_config.ini, .doorstop_version and changelog.txt.
The guides INSTALL-GK2COOP.txt and GK2Coop-Guides can go too.

Settings: BepInEx\\config\\com.fabio.gk2coop.cfg
This is a development build. Back up saves you care about.
"""

WORKSHOP['en'] = """Graveyard Keeper 2 Co-op - installing from the Steam Workshop

Steam downloads this mod but cannot switch it on. Copy it into the game once;
after that the mod installs its own updates when the game starts.

1. Subscribe, then close the game.
2. Open the download folder of this item:
     <Steam library>\\steamapps\\workshop\\content\\4358690\\<item id>
   (In Steam: right-click the game > Manage > Browse local files, go up two
   folders, then workshop\\content.)
3. Copy everything in that folder into the game folder, next to
   GraveyardKeeper2.exe, and allow merging folders.
4. Start the game. The main menu now has a "{B:Co-op}" button under the game's
   own buttons.

Everyone who plays together needs the mod in the same version. After Steam
updates the item, start the game once; the main menu says when the new version
was installed, and the next start uses it.

Uninstall: delete BepInEx\\plugins\\GK2Coop in the game folder.
"""

# ---------------------------------------------------------------- German
INSTALL['de'] = """Graveyard Keeper 2 Koop {VERSION}

INSTALLATION
1. Alle, die zusammen spielen, brauchen diese Mod in derselben Version.
2. Spiel schließen. Dieses Archiv in den Spielordner neben GraveyardKeeper2.exe
   entpacken und dabei die Ordner beibehalten.

ÜBER STEAM SPIELEN
3. Host: Im Hauptmenü auf „{B:Co-op}“ und dann „{B:Host a game}“ drücken. Die
   Spielerzahl wählen und „{B:Host on Steam}“ drücken. Einen Spielstand laden oder
   ein neues Spiel beginnen. Im Spiel lädt F10 Freunde ein.
4. Freunde: Die Einladung in Steam annehmen oder „{B:Co-op}“ > „{B:Join a game}“
   öffnen und neben dem Host „{B:Join}“ drücken. Ihr spielt in einer Kopie der
   Welt des Hosts; eure eigenen Spielstände bleiben unberührt.

OHNE STEAM SPIELEN
5. Host: „{B:Host a game}“ > „{B:Host without Steam}“ > „{B:Host}“. „{B:Show my addresses}“
   drücken und den anderen eine davon geben.
6. Freunde: „{B:Join a game}“ > „{B:Join by address}“, die Adresse eingeben,
   „{B:Join}“ drücken. Übers Internet muss der Router des Hosts den UDP-Port 8889
   weiterleiten, oder ihr nutzt ein VPN wie Tailscale.

TASTEN
T Chat - F6 Status - F7 Namensschilder - F10 Steam-Freunde einladen - F11 Aussehen ändern
Controller: Das Koop-Menü lässt sich damit bedienen; im Spiel bietet Pause >
„{B:Co-op}“, was die F-Tasten tun.

Gemeinsam: die Welt, Herstellung, Gärten, Zombies, Förderbänder, Händler, Ruf,
das Wetter, der Technologiebaum und die Edelsteine. Jeder behält sein eigenes
Inventar, Geld, Energie, Gesundheit, Aussehen, Talente und seinen Ort. Die Nacht
vergeht erst, wenn alle schlafen.
Der Host kann unter „{B:Host a game}“ „{B:I sleep}“ wählen; dann lässt sein Schlaf
die Nacht für alle vergehen.

Story-Szenen und Predigten: Beginnt bei einem Spieler eine Szene, werden die anderen
gefragt, ob sie mitschauen. Wer mitschaut, wird hingebracht und danach
zurückgebracht; „{B:Stop watching}“ beendet das vorzeitig. Tasten: Y
mitschauen, N weiterspielen oder aufhören; Controller: Y und B (B halten zum
Aufhören). Verpasst? Pause > „{B:Co-op}“ zeigt sie von Anfang an, solange sie
läuft.

KÄMPFE
Ein Kampf gehört einem Spieler; pro Welt läuft immer nur einer. Während jemand
kämpft, erfahren die anderen, wer kämpft und wie es ausging, können selbst
keinen Kampf beginnen, und die Uhr steht für alle still. Die Belohnung fällt
dem Kämpfer vor die Füße; der Fortschritt des Kampfgebiets gilt für alle.

NOCH NICHT IM KOOP
Gemeinsam kämpfen; die Antworten, die ein anderer Spieler im Gespräch wählt
(seine Sätze erscheinen in der Nähe); laufende Bestattungen und Autopsien (an
einem Grab oder Tisch arbeitet immer nur einer). Mit zwei bis vier Spielern getestet.
Mit einem Händler handelt immer nur einer: zwei Geschäfte im selben Moment
können beide sein letztes Stück bekommen.

DEINSTALLIEREN
BepInEx\\plugins\\GK2Coop im Spielordner löschen. Um auch BepInEx zu entfernen (wenn
keine andere Mod es nutzt), zusätzlich den Ordner BepInEx sowie winhttp.dll,
doorstop_config.ini, .doorstop_version und changelog.txt löschen.
Die Anleitungen INSTALL-GK2COOP.txt und GK2Coop-Guides können ebenfalls weg.

Einstellungen: BepInEx\\config\\com.fabio.gk2coop.cfg
Dies ist eine Entwicklungsversion. Sichere Spielstände, die dir wichtig sind.
"""

WORKSHOP['de'] = """Graveyard Keeper 2 Koop - Installation aus dem Steam-Workshop

Steam lädt diese Mod herunter, kann sie aber nicht einschalten. Kopiere sie einmal
ins Spiel; danach installiert die Mod ihre Updates beim Spielstart selbst.

1. Abonnieren, dann das Spiel schließen.
2. Den Download-Ordner dieses Eintrags öffnen:
     <Steam-Bibliothek>\\steamapps\\workshop\\content\\4358690\\<Eintrags-ID>
   (In Steam: Rechtsklick auf das Spiel > Verwalten > Lokale Dateien durchsuchen,
   zwei Ordner nach oben, dann workshop\\content.)
3. Alles aus diesem Ordner in den Spielordner neben GraveyardKeeper2.exe kopieren
   und das Zusammenführen der Ordner erlauben.
4. Spiel starten. Im Hauptmenü gibt es jetzt unter den Knöpfen des Spiels einen
   Knopf „{B:Co-op}“.

Alle, die zusammen spielen, brauchen die Mod in derselben Version. Nachdem Steam
den Eintrag aktualisiert hat, das Spiel einmal starten; das Hauptmenü meldet die
neue Version, und der nächste Start verwendet sie.

Deinstallieren: BepInEx\\plugins\\GK2Coop im Spielordner löschen.
"""

# ---------------------------------------------------------------- French
INSTALL['fr'] = """Graveyard Keeper 2 Coop {VERSION}

INSTALLATION
1. Tous ceux qui jouent ensemble ont besoin de ce mod, dans la même version.
2. Ferme le jeu. Extrais cette archive dans le dossier du jeu, à côté de
   GraveyardKeeper2.exe, en gardant ses dossiers.

JOUER VIA STEAM
3. Hôte : dans le menu principal, appuie sur « {B:Co-op} », puis « {B:Host a game} ».
   Choisis le nombre de joueurs et appuie sur « {B:Host on Steam} ». Charge une
   sauvegarde ou commence une partie. En jeu, F10 invite tes amis.
4. Amis : acceptez l'invitation dans Steam, ou ouvrez « {B:Co-op} » > « {B:Join a game} »
   et appuyez sur « {B:Join} » à côté de l'hôte. Vous jouez dans une copie du
   monde de l'hôte ; vos propres sauvegardes ne sont pas touchées.

JOUER SANS STEAM
5. Hôte : « {B:Host a game} » > « {B:Host without Steam} » > « {B:Host} ». Appuie sur
   « {B:Show my addresses} » et donne l'une d'elles aux autres.
6. Amis : « {B:Join a game} » > « {B:Join by address} », entrez l'adresse, appuyez sur
   « {B:Join} ». Par Internet, la box de l'hôte doit rediriger le port UDP 8889,
   ou utilisez un VPN comme Tailscale.

TOUCHES
T chat - F6 statut - F7 noms - F10 inviter des amis Steam - F11 changer d'apparence
Manette : le menu coop s'utilise avec elle ; en jeu, Pause > « {B:Co-op} »
offre ce que font les touches F.

Partagé : le monde, l'artisanat, les jardins, les zombies, les convoyeurs, les
marchands, la réputation, la météo, l'arbre technologique et les gemmes.
Chacun garde son inventaire, son argent, son énergie, sa santé, son apparence,
ses talents et sa position. La nuit ne passe que quand tout le monde dort.
L'hôte peut choisir « {B:I sleep} » dans « {B:Host a game} » ; son sommeil fait
alors passer la nuit pour tous.

Scènes de l'histoire et sermons : quand une scène commence pour un joueur, les autres
peuvent la regarder. Vous êtes emmené sur place puis ramené ensuite ; «
{B:Stop watching} » arrête plus tôt. Touches : Y regarder, N continuer ou
arrêter ; manette : Y et B (maintenir B pour arrêter). Manquée ? Pause > «
{B:Co-op} » la montre depuis le début tant qu'elle dure.

COMBATS
Un combat appartient à un seul joueur ; un seul à la fois par monde. Pendant
qu'un joueur combat, les autres voient qui combat et comment ça s'est terminé,
ne peuvent pas lancer de combat eux-mêmes, et l'horloge s'arrête pour tous.
Les récompenses tombent aux pieds du combattant ; la progression du niveau
est partagée.

PAS ENCORE EN COOP
Combattre ensemble ; les réponses qu'un autre joueur choisit dans une
conversation (ses répliques s'affichent à proximité) ; l'inhumation et
l'autopsie en cours (un seul joueur à la fois travaille une tombe ou une
table). Testé de deux à quatre joueurs.
Un seul joueur à la fois chez un marchand : deux échanges au même moment
peuvent obtenir tous deux son dernier article.

DÉSINSTALLER
Supprime BepInEx\\plugins\\GK2Coop dans le dossier du jeu. Pour retirer aussi BepInEx
(si aucun autre mod ne l'utilise), supprime aussi le dossier BepInEx,
winhttp.dll, doorstop_config.ini, .doorstop_version et changelog.txt.
Les guides INSTALL-GK2COOP.txt et GK2Coop-Guides peuvent aussi être supprimés.

Réglages : BepInEx\\config\\com.fabio.gk2coop.cfg
Ceci est une version de développement. Sauvegarde les parties auxquelles tu tiens.
"""

WORKSHOP['fr'] = """Graveyard Keeper 2 Coop - installation depuis le Workshop Steam

Steam télécharge ce mod mais ne peut pas l'activer. Copie-le une fois dans le jeu ;
ensuite, le mod installe lui-même ses mises à jour au lancement du jeu.

1. Abonne-toi, puis ferme le jeu.
2. Ouvre le dossier de téléchargement de cet objet :
     <bibliothèque Steam>\\steamapps\\workshop\\content\\4358690\\<id de l'objet>
   (Dans Steam : clic droit sur le jeu > Gérer > Parcourir les fichiers locaux,
   remonte de deux dossiers, puis workshop\\content.)
3. Copie tout le contenu de ce dossier dans le dossier du jeu, à côté de
   GraveyardKeeper2.exe, et accepte de fusionner les dossiers.
4. Lance le jeu. Le menu principal a maintenant un bouton « {B:Co-op} » sous les
   boutons du jeu.

Tous ceux qui jouent ensemble ont besoin du mod dans la même version. Après une
mise à jour Steam, lance le jeu une fois ; le menu principal indique la nouvelle
version, et le lancement suivant l'utilise.

Désinstaller : supprime BepInEx\\plugins\\GK2Coop dans le dossier du jeu.
"""

# ---------------------------------------------------------------- Spanish
INSTALL['es'] = """Graveyard Keeper 2 Cooperativo {VERSION}

INSTALACIÓN
1. Todos los que juegan juntos necesitan este mod, en la misma versión.
2. Cierra el juego. Extrae este archivo en la carpeta del juego, junto a
   GraveyardKeeper2.exe, manteniendo sus carpetas.

JUGAR POR STEAM
3. Anfitrión: en el menú principal pulsa «{B:Co-op}» y luego «{B:Host a game}». Elige
   el número de jugadores y pulsa «{B:Host on Steam}». Carga una partida o empieza
   una nueva. En el juego, F10 invita a tus amigos.
4. Amigos: aceptad la invitación en Steam, o abrid «{B:Co-op}» > «{B:Join a game}» y
   pulsad «{B:Join}» junto al anfitrión. Jugáis en una copia del mundo del
   anfitrión; vuestras partidas guardadas no se tocan.

JUGAR SIN STEAM
5. Anfitrión: «{B:Host a game}» > «{B:Host without Steam}» > «{B:Host}». Pulsa
   «{B:Show my addresses}» y pasa una de ellas a los demás.
6. Amigos: «{B:Join a game}» > «{B:Join by address}», escribid la dirección y pulsad
   «{B:Join}». Por internet, el router del anfitrión debe redirigir el puerto UDP
   8889, o usad una VPN como Tailscale.

TECLAS
T chat - F6 estado - F7 nombres - F10 invitar amigos de Steam - F11 cambiar tu aspecto
Mando: el menú cooperativo funciona con él; en la partida, Pausa > «{B:Co-op}»
ofrece lo que hacen las teclas F.

Compartido: el mundo, la fabricación, los huertos, los zombis, las cintas, los
comerciantes, la reputación, el tiempo, el árbol tecnológico y las gemas.
Cada jugador conserva su inventario, dinero, energía, salud, aspecto, talentos y
posición. La noche solo pasa cuando todos duermen.
El anfitrión puede elegir «{B:I sleep}» en «{B:Host a game}»; entonces su sueño
hace pasar la noche para todos.

Escenas de la historia y sermones: cuando empieza una escena para un jugador, se pregunta
a los demás si quieren verla. Te lleva allí y luego te devuelve;
«{B:Stop watching}» la termina antes. Teclas: Y ver, N seguir jugando o parar;
mando: Y y B (mantén B para parar). ¿Te la perdiste? Pausa > «{B:Co-op}» la
muestra desde el principio mientras dure.

COMBATES
Un combate es de un solo jugador, y solo hay uno a la vez por mundo. Mientras
alguien combate, los demás ven quién combate y cómo terminó, no pueden empezar
un combate propio y el reloj se detiene para todos. Las recompensas caen a los
pies de quien combate; el progreso del nivel es compartido.

AÚN NO EN COOPERATIVO
Combatir juntos; las respuestas que otro jugador elige en una conversación
(sus frases se ven cerca); el entierro y la autopsia en curso (solo un jugador
a la vez trabaja una tumba o una mesa). Probado con dos a cuatro jugadores.
Comerciad con un vendedor de uno en uno: dos tratos al mismo tiempo pueden
llevarse ambos su último artículo.

DESINSTALAR
Borra BepInEx\\plugins\\GK2Coop en la carpeta del juego. Para quitar también BepInEx
(si ningún otro mod lo usa), borra además la carpeta BepInEx, winhttp.dll,
doorstop_config.ini, .doorstop_version y changelog.txt.
Las guías INSTALL-GK2COOP.txt y GK2Coop-Guides también se pueden borrar.

Ajustes: BepInEx\\config\\com.fabio.gk2coop.cfg
Esta es una versión en desarrollo. Haz copia de las partidas que te importen.
"""

WORKSHOP['es'] = """Graveyard Keeper 2 Cooperativo - instalación desde el Workshop de Steam

Steam descarga este mod pero no puede activarlo. Cópialo una vez en el juego;
después, el mod instala sus propias actualizaciones al iniciar el juego.

1. Suscríbete y cierra el juego.
2. Abre la carpeta de descarga de este elemento:
     <biblioteca de Steam>\\steamapps\\workshop\\content\\4358690\\<id del elemento>
   (En Steam: clic derecho en el juego > Administrar > Ver archivos locales, sube
   dos carpetas y luego workshop\\content.)
3. Copia todo el contenido de esa carpeta en la carpeta del juego, junto a
   GraveyardKeeper2.exe, y acepta combinar las carpetas.
4. Inicia el juego. El menú principal tiene ahora un botón «{B:Co-op}» debajo de
   los botones del juego.

Todos los que juegan juntos necesitan el mod en la misma versión. Cuando Steam
actualice el elemento, inicia el juego una vez; el menú principal avisa de la
nueva versión y el siguiente inicio la usa.

Desinstalar: borra BepInEx\\plugins\\GK2Coop en la carpeta del juego.
"""

# ---------------------------------------------------------------- Brazilian Portuguese
INSTALL['pt-br'] = """Graveyard Keeper 2 Co-op {VERSION}

INSTALAÇÃO
1. Todos que jogam juntos precisam deste mod, na mesma versão.
2. Feche o jogo. Extraia este arquivo na pasta do jogo, ao lado de
   GraveyardKeeper2.exe, mantendo as pastas.

JOGAR PELA STEAM
3. Anfitrião: no menu principal, aperte "{B:Co-op}" e depois "{B:Host a game}".
   Escolha o número de jogadores e aperte "{B:Host on Steam}". Carregue um save ou
   comece um jogo novo. No jogo, F10 convida amigos.
4. Amigos: aceitem o convite na Steam, ou abram "{B:Co-op}" > "{B:Join a game}" e
   apertem "{B:Join}" ao lado do anfitrião. Vocês jogam em uma cópia do mundo do
   anfitrião; seus próprios saves não são alterados.

JOGAR SEM A STEAM
5. Anfitrião: "{B:Host a game}" > "{B:Host without Steam}" > "{B:Host}". Aperte
   "{B:Show my addresses}" e passe um deles para os outros.
6. Amigos: "{B:Join a game}" > "{B:Join by address}", digitem o endereço e apertem
   "{B:Join}". Pela internet, o roteador do anfitrião precisa redirecionar a porta
   UDP 8889, ou usem uma VPN como o Tailscale.

TECLAS
T chat - F6 status - F7 nomes - F10 convidar amigos da Steam - F11 mudar a aparência
Controle: o menu co-op funciona com ele; no jogo, Pausa > "{B:Co-op}" oferece
o que as teclas F fazem.

Compartilhado: o mundo, a produção, as hortas, os zumbis, as esteiras, os
vendedores, a reputação, o clima, a árvore tecnológica e as gemas. Cada
jogador mantém seu inventário, dinheiro, energia, saúde, aparência, talentos e
posição. A noite só passa quando todos dormem.
O anfitrião pode escolher "{B:I sleep}" em "{B:Host a game}"; então o sono dele
faz a noite passar para todos.

Cenas da história e sermões: quando uma cena começa para um jogador, os outros são
perguntados se querem assistir. Você é levado até lá e trazido de volta
depois; "{B:Stop watching}" encerra antes. Teclas: Y assistir, N continuar ou
parar; controle: Y e B (segure B para parar). Perdeu? Pausa > "{B:Co-op}"
mostra desde o início enquanto ela durar.

LUTAS
Uma luta é de um jogador só, e há uma de cada vez por mundo. Enquanto alguém
luta, os outros veem quem está lutando e como terminou, não podem começar uma
luta própria e o relógio fica parado para todos. As recompensas caem aos pés
de quem lutou; o progresso da fase é compartilhado.

AINDA NÃO NO CO-OP
Lutar juntos; as respostas que outro jogador escolhe numa conversa (as falas
dele aparecem por perto); enterro e autópsia em andamento (só um jogador por
vez trabalha numa cova ou mesa). Testado com dois a quatro jogadores.
Negociem com um vendedor um de cada vez: duas trocas ao mesmo tempo podem
levar as duas o último item dele.

DESINSTALAR
Apague BepInEx\\plugins\\GK2Coop na pasta do jogo. Para remover também o BepInEx (se
nenhum outro mod o usa), apague também a pasta BepInEx, winhttp.dll,
doorstop_config.ini, .doorstop_version e changelog.txt.
Os guias INSTALL-GK2COOP.txt e GK2Coop-Guides também podem ser apagados.

Configurações: BepInEx\\config\\com.fabio.gk2coop.cfg
Esta é uma versão em desenvolvimento. Faça backup dos saves que importam para você.
"""

WORKSHOP['pt-br'] = """Graveyard Keeper 2 Co-op - instalação pelo Workshop da Steam

A Steam baixa este mod, mas não consegue ativá-lo. Copie-o uma vez para o jogo;
depois disso, o mod instala as próprias atualizações quando o jogo inicia.

1. Inscreva-se e feche o jogo.
2. Abra a pasta de download deste item:
     <biblioteca Steam>\\steamapps\\workshop\\content\\4358690\\<id do item>
   (Na Steam: clique com o botão direito no jogo > Gerenciar > Explorar arquivos
   locais, suba duas pastas e depois workshop\\content.)
3. Copie tudo dessa pasta para a pasta do jogo, ao lado de GraveyardKeeper2.exe,
   e permita mesclar as pastas.
4. Inicie o jogo. O menu principal agora tem um botão "{B:Co-op}" abaixo dos
   botões do jogo.

Todos que jogam juntos precisam do mod na mesma versão. Depois que a Steam
atualizar o item, inicie o jogo uma vez; o menu principal avisa sobre a nova
versão, e a próxima inicialização a usa.

Desinstalar: apague BepInEx\\plugins\\GK2Coop na pasta do jogo.
"""

# ---------------------------------------------------------------- Polish
INSTALL['pl'] = """Graveyard Keeper 2 Co-op {VERSION}

INSTALACJA
1. Każdy, kto gra razem, potrzebuje tego moda w tej samej wersji.
2. Zamknij grę. Rozpakuj to archiwum do folderu gry, obok GraveyardKeeper2.exe,
   zachowując foldery.

GRA PRZEZ STEAM
3. Gospodarz: w menu głównym naciśnij „{B:Co-op}”, a potem „{B:Host a game}”. Wybierz
   liczbę graczy i naciśnij „{B:Host on Steam}”. Wczytaj zapis albo zacznij nową
   grę. W grze F10 zaprasza znajomych.
4. Znajomi: przyjmijcie zaproszenie na Steamie albo otwórzcie „{B:Co-op}” >
   „{B:Join a game}” i naciśnijcie „{B:Join}” obok gospodarza. Gracie w kopii świata
   gospodarza; wasze własne zapisy pozostają nietknięte.

GRA BEZ STEAM
5. Gospodarz: „{B:Host a game}” > „{B:Host without Steam}” > „{B:Host}”. Naciśnij
   „{B:Show my addresses}” i podaj jeden z nich pozostałym.
6. Znajomi: „{B:Join a game}” > „{B:Join by address}”, wpiszcie adres i naciśnijcie
   „{B:Join}”. Przez internet router gospodarza musi przekierować port UDP 8889,
   albo użyjcie VPN takiego jak Tailscale.

KLAWISZE
T czat - F6 stan - F7 imiona - F10 zaproś znajomych ze Steam - F11 zmień wygląd
Pad: menu co-op działa z padem; w grze Pauza > „{B:Co-op}” daje to, co
klawisze F.

Wspólne: świat, wytwarzanie, ogrody, zombie, przenośniki, handlarze, reputacja,
pogoda, drzewko technologii i klejnoty. Każdy zachowuje własny ekwipunek,
pieniądze, energię, zdrowie, wygląd, talenty i pozycję. Noc mija dopiero, gdy
wszyscy śpią.
Gospodarz może wybrać „{B:I sleep}” w „{B:Host a game}”; wtedy jego sen sprawia,
że noc mija dla wszystkich.

Sceny fabularne i kazania: gdy u jednego gracza zaczyna się scena, pozostali mogą ją
obejrzeć. Oglądający zostaje tam przeniesiony, a potem wraca;
„{B:Stop watching}” kończy wcześniej. Klawisze: Y oglądaj, N graj dalej lub
przestań; pad: Y i B (przytrzymaj B, by przestać). Przegapiona? Pauza >
„{B:Co-op}” pokaże ją od początku, póki trwa.

WALKI
Walkę toczy jeden gracz; w jednym świecie trwa naraz tylko jedna. Gdy ktoś
walczy, pozostali widzą, kto walczy i jak się skończyło, nie mogą zacząć
własnej walki, a zegar stoi dla wszystkich. Nagrody spadają pod nogi
walczącego; postęp poziomu jest wspólny.

JESZCZE NIE W CO-OP
Wspólna walka; odpowiedzi, które inny gracz wybiera w rozmowie (jego kwestie
widać w pobliżu); pochówek i sekcja w toku (przy grobie lub stole pracuje
naraz tylko jeden gracz). Przetestowane z dwoma do czterech graczy.
Z jednym handlarzem handluje naraz tylko jeden gracz: dwie transakcje w tej
samej chwili mogą obie dostać jego ostatni przedmiot.

ODINSTALOWANIE
Usuń BepInEx\\plugins\\GK2Coop w folderze gry. Aby usunąć też BepInEx (jeśli nie używa
go inny mod), usuń również folder BepInEx, winhttp.dll, doorstop_config.ini,
.doorstop_version i changelog.txt.
Przewodniki INSTALL-GK2COOP.txt i GK2Coop-Guides też można usunąć.

Ustawienia: BepInEx\\config\\com.fabio.gk2coop.cfg
To wersja rozwojowa. Zrób kopię zapisów, na których ci zależy.
"""

WORKSHOP['pl'] = """Graveyard Keeper 2 Co-op - instalacja z Warsztatu Steam

Steam pobiera tego moda, ale nie może go włączyć. Skopiuj go raz do gry; potem
mod sam instaluje swoje aktualizacje przy uruchomieniu gry.

1. Zasubskrybuj, a potem zamknij grę.
2. Otwórz folder pobierania tego elementu:
     <biblioteka Steam>\\steamapps\\workshop\\content\\4358690\\<id elementu>
   (W Steamie: prawy klik na grze > Zarządzaj > Przeglądaj pliki lokalne, przejdź
   dwa foldery wyżej, potem workshop\\content.)
3. Skopiuj całą zawartość tego folderu do folderu gry, obok GraveyardKeeper2.exe,
   i zgódź się na scalenie folderów.
4. Uruchom grę. W menu głównym jest teraz przycisk „{B:Co-op}” pod przyciskami gry.

Każdy, kto gra razem, potrzebuje moda w tej samej wersji. Gdy Steam zaktualizuje
element, uruchom grę raz; menu główne poinformuje o nowej wersji, a następne
uruchomienie jej użyje.

Odinstalowanie: usuń BepInEx\\plugins\\GK2Coop w folderze gry.
"""

# ---------------------------------------------------------------- Russian
INSTALL['ru'] = """Graveyard Keeper 2 Кооператив {VERSION}

УСТАНОВКА
1. Всем, кто играет вместе, нужен этот мод одной и той же версии.
2. Закрой игру. Распакуй этот архив в папку игры, рядом с GraveyardKeeper2.exe,
   сохранив папки.

ИГРА ЧЕРЕЗ STEAM
3. Хост: в главном меню нажми «{B:Co-op}», затем «{B:Host a game}». Выбери число
   игроков и нажми «{B:Host on Steam}». Загрузи сохранение или начни новую игру.
   В игре F10 приглашает друзей.
4. Друзья: примите приглашение в Steam или откройте «{B:Co-op}» > «{B:Join a game}»
   и нажмите «{B:Join}» рядом с хостом. Вы играете в копии мира хоста; ваши
   собственные сохранения не затрагиваются.

ИГРА БЕЗ STEAM
5. Хост: «{B:Host a game}» > «{B:Host without Steam}» > «{B:Host}». Нажми
   «{B:Show my addresses}» и дай один из них остальным.
6. Друзья: «{B:Join a game}» > «{B:Join by address}», введите адрес и нажмите
   «{B:Join}». Через интернет роутер хоста должен перенаправлять UDP-порт 8889,
   или используйте VPN вроде Tailscale.

КЛАВИШИ
T чат - F6 состояние - F7 имена - F10 пригласить друзей из Steam - F11 сменить облик
Геймпад: меню кооператива работает с ним; в игре Пауза > «{B:Co-op}» даёт то,
что делают клавиши F.

Общее: мир, производство, огороды, зомби, конвейеры, торговцы, репутация, погода,
древо технологий и самоцветы. У каждого свой инвентарь, деньги, энергия,
здоровье, облик, таланты и позиция. Ночь проходит, только когда уснут все.
Хост может выбрать «{B:I sleep}» в «{B:Host a game}»; тогда его сон проводит
ночь для всех.

Сюжетные сцены и проповеди: когда у одного игрока начинается сцена, остальным предлагают
её посмотреть. Зрителя переносят туда и потом обратно; «{B:Stop watching}»
завершает просмотр раньше. Клавиши: Y смотреть, N играть дальше или
прекратить; геймпад: Y и B (удерживайте B, чтобы прекратить). Пропустили?
Пауза > «{B:Co-op}» покажет её с начала, пока она идёт.

БОИ
Бой ведёт один игрок; в мире идёт только один бой одновременно. Пока кто-то
сражается, остальные видят, кто сражается и чем всё закончилось, не могут
начать свой бой, а часы стоят у всех. Награда падает к ногам сражавшегося;
прогресс уровня общий.

ПОКА НЕ В КООПЕРАТИВЕ
Сражаться вместе; ответы, которые другой игрок выбирает в разговоре (его
реплики видны рядом); погребение и вскрытие в процессе (у могилы или стола
работает только один игрок). Проверено с двумя–четырьмя игроками.
Торгуйте с торговцем по одному: две сделки в один и тот же момент могут обе
получить его последний товар.

УДАЛЕНИЕ
Удали BepInEx\\plugins\\GK2Coop в папке игры. Чтобы удалить и BepInEx (если его не
использует другой мод), удали также папку BepInEx, winhttp.dll,
doorstop_config.ini, .doorstop_version и changelog.txt.
Инструкции INSTALL-GK2COOP.txt и GK2Coop-Guides тоже можно удалить.

Настройки: BepInEx\\config\\com.fabio.gk2coop.cfg
Это версия в разработке. Сделай копию сохранений, которыми дорожишь.
"""

WORKSHOP['ru'] = """Graveyard Keeper 2 Кооператив - установка из Мастерской Steam

Steam скачивает этот мод, но не может его включить. Скопируй его в игру один раз;
после этого мод сам устанавливает свои обновления при запуске игры.

1. Подпишись, затем закрой игру.
2. Открой папку загрузки этого предмета:
     <библиотека Steam>\\steamapps\\workshop\\content\\4358690\\<id предмета>
   (В Steam: правый клик по игре > Управление > Просмотреть локальные файлы,
   поднимись на две папки выше, затем workshop\\content.)
3. Скопируй всё из этой папки в папку игры, рядом с GraveyardKeeper2.exe, и
   разреши объединить папки.
4. Запусти игру. В главном меню теперь есть кнопка «{B:Co-op}» под кнопками игры.

Всем, кто играет вместе, нужен мод одной версии. Когда Steam обновит предмет,
запусти игру один раз; главное меню сообщит о новой версии, и следующий запуск
её использует.

Удаление: удали BepInEx\\plugins\\GK2Coop в папке игры.
"""

# ---------------------------------------------------------------- Turkish
INSTALL['tr'] = """Graveyard Keeper 2 Ortak Oyun {VERSION}

KURULUM
1. Birlikte oynayan herkesin bu modun aynı sürümüne ihtiyacı var.
2. Oyunu kapat. Bu arşivi klasörleriyle birlikte oyun klasörüne,
   GraveyardKeeper2.exe dosyasının yanına çıkar.

STEAM ÜZERİNDEN OYNAMAK
3. Kurucu: ana menüde "{B:Co-op}", sonra "{B:Host a game}" düğmesine bas. Oyuncu
   sayısını seç ve "{B:Host on Steam}" düğmesine bas. Bir kayıt yükle ya da yeni
   oyun başlat. Oyunda F10 arkadaşlarını davet eder.
4. Arkadaşlar: Steam'deki daveti kabul edin ya da "{B:Co-op}" > "{B:Join a game}"
   açıp kurucunun yanındaki "{B:Join}" düğmesine basın. Kurucunun dünyasının bir
   kopyasında oynarsınız; kendi kayıtlarınıza dokunulmaz.

STEAM OLMADAN OYNAMAK
5. Kurucu: "{B:Host a game}" > "{B:Host without Steam}" > "{B:Host}". "{B:Show my addresses}"
   düğmesine bas ve birini diğerlerine ver.
6. Arkadaşlar: "{B:Join a game}" > "{B:Join by address}", adresi girin, "{B:Join}"
   düğmesine basın. İnternet üzerinden kurucunun modemi UDP 8889 portunu
   yönlendirmeli ya da Tailscale gibi bir VPN kullanın.

TUŞLAR
T sohbet - F6 durum - F7 isimler - F10 Steam arkadaşlarını davet et - F11 görünümü değiştir
Kumanda: ortak oyun menüsü kumandayla çalışır; oyunda Duraklat > "{B:Co-op}" F
tuşlarının yaptıklarını sunar.

Ortak olanlar: dünya, üretim, bahçeler, zombiler, konveyörler, tüccarlar, itibar,
hava durumu, teknoloji ağacı ve değerli taşlar. Herkes kendi envanterini, parasını,
enerjisini, sağlığını, görünümünü, yeteneklerini ve konumunu korur. Gece ancak
herkes uyuyunca geçer.
Kurucu "{B:Host a game}" içinde "{B:I sleep}" seçeneğini seçebilir; o zaman
kurucunun uykusu geceyi herkes için geçirir.

Hikâye sahneleri ve vaazlar: Bir oyuncunun sahnesi başladığında diğerlerine izlemek
isteyip istemedikleri sorulur. İzleyen oraya götürülür, sonra geri getirilir;
"{B:Stop watching}" erken bitirir. Tuşlar: Y izle, N devam et veya bırak;
kumanda: Y ve B (bırakmak için B basılı tut). Kaçırdın mı? Duraklat >
"{B:Co-op}" sahne sürdükçe baştan gösterir.

DÖVÜŞLER
Bir dövüşü tek bir oyuncu yapar; her dünyada aynı anda yalnızca bir dövüş olur.
Biri dövüşürken diğerleri kimin dövüştüğünü ve nasıl bittiğini görür, kendi
dövüşlerini başlatamaz ve saat herkes için durur. Ödüller dövüşenin ayağının
dibine düşer; bölümün ilerlemesi ortaktır.

HENÜZ ORTAK OYUNDA YOK
Birlikte dövüşmek; başka bir oyuncunun bir konuşmada seçtiği cevaplar (onun
sözleri yakında görünür); süren defin ve otopsi işleri (bir mezarda veya
masada aynı anda yalnızca bir oyuncu çalışır). İki ila dört oyuncuyla test edildi.
Bir satıcıyla aynı anda tek oyuncu ticaret yapsın: aynı anda yapılan iki
takas, satıcının son eşyasını ikisine de verebilir.

KALDIRMA
Oyun klasöründeki BepInEx\\plugins\\GK2Coop klasörünü sil. BepInEx'i de kaldırmak için
(başka bir mod kullanmıyorsa) BepInEx klasörünü, winhttp.dll,
doorstop_config.ini, .doorstop_version ve changelog.txt dosyalarını da sil.
INSTALL-GK2COOP.txt ve GK2Coop-Guides kılavuzları da silinebilir.

Ayarlar: BepInEx\\config\\com.fabio.gk2coop.cfg
Bu bir geliştirme sürümüdür. Önemli kayıtlarını yedekle.
"""

WORKSHOP['tr'] = """Graveyard Keeper 2 Ortak Oyun - Steam Atölyesi'nden kurulum

Steam bu modu indirir ama etkinleştiremez. Onu bir kez oyuna kopyala; sonrasında
mod, oyun başlarken kendi güncellemelerini kendisi kurar.

1. Abone ol, sonra oyunu kapat.
2. Bu öğenin indirme klasörünü aç:
     <Steam kütüphanesi>\\steamapps\\workshop\\content\\4358690\\<öğe kimliği>
   (Steam'de: oyuna sağ tıkla > Yönet > Yerel dosyalara göz at, iki klasör
   yukarı çık, sonra workshop\\content.)
3. O klasördeki her şeyi oyun klasörüne, GraveyardKeeper2.exe dosyasının yanına
   kopyala ve klasörlerin birleştirilmesine izin ver.
4. Oyunu başlat. Ana menüde artık oyunun düğmelerinin altında bir "{B:Co-op}"
   düğmesi var.

Birlikte oynayan herkesin modun aynı sürümüne ihtiyacı var. Steam öğeyi
güncelledikten sonra oyunu bir kez başlat; ana menü yeni sürümü bildirir ve bir
sonraki açılış onu kullanır.

Kaldırma: oyun klasöründeki BepInEx\\plugins\\GK2Coop klasörünü sil.
"""

# ---------------------------------------------------------------- Japanese
INSTALL['ja'] = """Graveyard Keeper 2 協力プレイ {VERSION}

インストール
1. 一緒に遊ぶ全員が、同じバージョンのこのMODを入れる必要があります。
2. ゲームを終了し、このアーカイブをフォルダ構成のまま、ゲームフォルダの
   GraveyardKeeper2.exe と同じ場所に展開してください。

Steamで遊ぶ
3. ホスト：メインメニューで「{B:Co-op}」→「{B:Host a game}」を押します。人数を
   選んで「{B:Host on Steam}」を押し、セーブデータをロードするか新しいゲームを
   始めます。ゲーム中は F10 でフレンドを招待できます。
4. フレンド：Steamで招待を受けるか、「{B:Co-op}」→「{B:Join a game}」を開いて
   ホストの横の「{B:Join}」を押します。ホストの世界のコピーで遊ぶので、
   自分のセーブデータはそのままです。

Steamを使わずに遊ぶ
5. ホスト：「{B:Host a game}」→「{B:Host without Steam}」→「{B:Host}」。
   「{B:Show my addresses}」を押して、表示されたアドレスをほかの人に伝えます。
6. フレンド：「{B:Join a game}」→「{B:Join by address}」でアドレスを入力し、
   「{B:Join}」を押します。インターネット経由の場合は、ホストのルーターで
   UDPポート8889を転送するか、TailscaleなどのVPNを使ってください。

キー
T チャット - F6 状態 - F7 名前表示 - F10 Steamのフレンドを招待 - F11 見た目の変更
コントローラー：協力プレイメニューはコントローラーで操作できます。ゲーム中はポーズ >「{B:Co-op}」でFキーの機能を使えます。

共有されるもの：世界、クラフト、畑、ゾンビ、コンベア、商人、評判、天気、
技術ツリー、宝石。インベントリ、お金、エネルギー、体力、見た目、
才能、位置は各自のものです。夜は全員が眠ったときだけ明けます。
ホストは「{B:Host a game}」で「{B:I sleep}」を選べます。その場合、ホストが眠ると
全員の夜が明けます。

ストーリーシーンと説教：誰かのシーンが始まると、他のプレイヤーに一緒に見るか尋ねます。
見る場合はその場所へ移動し、終わると元の場所に戻ります。
「{B:Stop watching}」で途中でやめられます。
キー：Y 見る、N 続ける／やめる。コントローラー：Y と B（B 長押しでやめる）。
見逃したら、シーンの間はポーズ >「{B:Co-op}」から最初から見られます。

戦闘
戦闘は1人のプレイヤーのもので、1つの世界で同時に行えるのは1つだけです。
誰かが戦っている間、他のプレイヤーには誰が戦っているか、どう終わったかが
表示されます。自分で戦闘を始めることはできず、時間は全員止まります。
報酬は戦ったプレイヤーの足元に落ち、レベルの進行は共有されます。

協力プレイでまだできないこと
一緒に戦うこと、他のプレイヤーが会話で選んだ答え（セリフは近くに表示されます）、
進行中の埋葬と解剖（墓や台で作業できるのは一度に1人です）。2〜4人でテスト済みです。
商人との取引は一人ずつ行ってください。同時に二人が取引すると、最後の品物が二人とも手に入ることがあります。

アンインストール
ゲームフォルダの BepInEx\\plugins\\GK2Coop を削除します。BepInEx も消す場合
（ほかのMODが使っていなければ）、BepInEx フォルダ、winhttp.dll、
doorstop_config.ini、.doorstop_version、changelog.txt も削除します。
説明書の INSTALL-GK2COOP.txt と GK2Coop-Guides も削除できます。

設定：BepInEx\\config\\com.fabio.gk2coop.cfg
これは開発版です。大切なセーブデータはバックアップしてください。
"""

WORKSHOP['ja'] = """Graveyard Keeper 2 協力プレイ - Steamワークショップからのインストール

SteamはこのMODをダウンロードしますが、有効にはできません。一度だけゲームに
コピーしてください。その後はゲーム起動時にMODが自分で更新をインストールします。

1. サブスクライブしてから、ゲームを終了します。
2. このアイテムのダウンロードフォルダを開きます：
     <Steamライブラリ>\\steamapps\\workshop\\content\\4358690\\<アイテムID>
   （Steamでゲームを右クリック > 管理 > ローカルファイルを閲覧 を開き、
   2つ上のフォルダに戻ってから workshop\\content へ。）
3. そのフォルダの中身をすべて、ゲームフォルダの GraveyardKeeper2.exe と同じ
   場所にコピーし、フォルダの統合を許可します。
4. ゲームを起動します。メインメニューのゲームのボタンの下に「{B:Co-op}」
   ボタンが追加されています。

一緒に遊ぶ全員が同じバージョンのMODを必要とします。Steamがアイテムを更新
したら一度ゲームを起動してください。メインメニューに新しいバージョンが
表示され、次の起動から使われます。

アンインストール：ゲームフォルダの BepInEx\\plugins\\GK2Coop を削除します。
"""

# ---------------------------------------------------------------- Simplified Chinese
INSTALL['zh_cn'] = """Graveyard Keeper 2 联机合作 {VERSION}

安装
1. 一起玩的每个人都需要安装相同版本的这个模组。
2. 关闭游戏。把这个压缩包解压到游戏文件夹中 GraveyardKeeper2.exe 旁边，
   保留其中的文件夹结构。

通过 Steam 游玩
3. 房主：在主菜单按“{B:Co-op}”，再按“{B:Host a game}”。选择人数后按
   “{B:Host on Steam}”。载入存档或开始新游戏。在游戏中按 F10 邀请好友。
4. 好友：在 Steam 中接受邀请，或打开“{B:Co-op}”>“{B:Join a game}”，在房主旁边按
   “{B:Join}”。你们会在房主世界的副本中游玩，自己的存档不会被改动。

不通过 Steam 游玩
5. 房主：“{B:Host a game}”>“{B:Host without Steam}”>“{B:Host}”。按
   “{B:Show my addresses}”，把其中一个地址告诉其他人。
6. 好友：“{B:Join a game}”>“{B:Join by address}”，输入地址后按“{B:Join}”。
   通过互联网连接时，房主的路由器需要转发 UDP 端口 8889，或者使用 Tailscale
   这类 VPN。

按键
T 聊天 - F6 状态 - F7 名字 - F10 邀请 Steam 好友 - F11 更改外观
手柄：联机菜单可用手柄操作；游戏中，暂停 >“{B:Co-op}”提供 F 键的功能。

共享的内容：世界、制作、菜园、僵尸、传送带、商人、声望、天气、科技树和宝石。
每个人保留自己的物品栏、金钱、体力、生命、外观、天赋和位置。只有所有人都睡着，
夜晚才会过去。
房主可以在“{B:Host a game}”中选择“{B:I sleep}”，这样房主睡觉时所有人的夜晚
都会过去。

剧情场景与布道：某位玩家的剧情开始时，会询问其他玩家是否一起观看。
观看时会被带到现场，结束后返回原处；“{B:Stop watching}”可提前结束。
按键：Y 观看，N 继续游戏或停止；手柄：Y 和 B（按住 B 停止）。
错过了？剧情进行时可在 暂停 >“{B:Co-op}”从头观看。

战斗
战斗属于一位玩家，每个世界同一时间只能有一场战斗。有人战斗时，其他人会看到谁在
战斗以及结果如何，不能自己开始战斗，所有人的时间都会停止。奖励掉落在战斗者脚下；
关卡进度是共享的。

联机中尚不支持
一起战斗；其他玩家在对话中选择的回答（他们的台词会显示在附近）；进行中的埋葬和
解剖（一座坟墓或一张桌子同一时间只能由一位玩家操作）。已在两到四人游戏中测试。
请一次只让一名玩家与同一个商人交易：同时进行的两笔交易可能都拿到他的最后一件物品。

卸载
删除游戏文件夹中的 BepInEx\\plugins\\GK2Coop。如果还要移除 BepInEx（且没有其他模组
在用），再删除 BepInEx 文件夹、winhttp.dll、doorstop_config.ini、
.doorstop_version 和 changelog.txt。
说明文件 INSTALL-GK2COOP.txt 和 GK2Coop-Guides 也可以删除。

设置：BepInEx\\config\\com.fabio.gk2coop.cfg
这是开发版本。请备份你在意的存档。
"""

WORKSHOP['zh_cn'] = """Graveyard Keeper 2 联机合作 - 从 Steam 创意工坊安装

Steam 会下载这个模组，但无法启用它。请把它复制到游戏中一次；之后模组会在游戏
启动时自己安装更新。

1. 订阅，然后关闭游戏。
2. 打开这个物品的下载文件夹：
     <Steam 库>\\steamapps\\workshop\\content\\4358690\\<物品 ID>
   （在 Steam 中右键点击游戏 > 管理 > 浏览本地文件，向上两级文件夹，再进入
   workshop\\content。）
3. 把该文件夹中的所有内容复制到游戏文件夹中 GraveyardKeeper2.exe 旁边，并允许
   合并文件夹。
4. 启动游戏。主菜单中游戏按钮的下方现在多了一个“{B:Co-op}”按钮。

一起玩的每个人都需要相同版本的模组。Steam 更新物品后，启动一次游戏；主菜单
会提示新版本，下次启动时生效。

卸载：删除游戏文件夹中的 BepInEx\\plugins\\GK2Coop。
"""

# ---------------------------------------------------------------- Korean
INSTALL['ko'] = """Graveyard Keeper 2 협동 {VERSION}

설치
1. 함께 플레이하는 모두가 같은 버전의 이 모드가 필요합니다.
2. 게임을 종료하고, 이 압축 파일을 폴더 구조 그대로 게임 폴더의
   GraveyardKeeper2.exe 옆에 풉니다.

Steam으로 플레이
3. 호스트: 메인 메뉴에서 "{B:Co-op}", 이어서 "{B:Host a game}" 버튼을 누릅니다. 인원을
   고르고 "{B:Host on Steam}" 버튼을 누른 뒤 저장 파일을 불러오거나 새 게임을
   시작합니다. 게임 중에는 F10으로 친구를 초대합니다.
4. 친구: Steam에서 초대를 수락하거나 "{B:Co-op}" > "{B:Join a game}" 메뉴를 열고 호스트
   옆의 "{B:Join}" 버튼을 누릅니다. 호스트 세계의 사본에서 플레이하므로 내 저장
   파일은 그대로입니다.

Steam 없이 플레이
5. 호스트: "{B:Host a game}" > "{B:Host without Steam}" > "{B:Host}". "{B:Show my addresses}" 버튼을
   눌러 나온 주소 중 하나를 다른 사람에게 알려 줍니다.
6. 친구: "{B:Join a game}" > "{B:Join by address}"에서 주소를 입력하고 "{B:Join}" 버튼을
   누릅니다. 인터넷으로는 호스트의 공유기가 UDP 포트 8889를 포워딩해야 하며,
   Tailscale 같은 VPN을 써도 됩니다.

키
T 채팅 - F6 상태 - F7 이름표 - F10 Steam 친구 초대 - F11 외형 변경
컨트롤러: 협동 메뉴를 컨트롤러로 사용할 수 있습니다. 게임 중에는 일시 정지 > "{B:Co-op}"에서 F 키 기능을 쓸 수 있습니다.

공유되는 것: 세계, 제작, 텃밭, 좀비, 컨베이어, 상인, 평판, 날씨, 기술 트리,
보석. 인벤토리, 돈, 에너지, 체력, 외형, 재능, 위치는 각자의 것입니다.
밤은 모두가 잠들어야 지나갑니다.
호스트는 "{B:Host a game}"에서 "{B:I sleep}"을 고를 수 있습니다. 그러면 호스트가 자면
모두의 밤이 지나갑니다.

스토리 장면과 설교: 한 플레이어의 장면이 시작되면 다른 플레이어에게 함께 볼지 묻습니다.
보면 그 장소로 이동했다가 끝나면 돌아옵니다. "{B:Stop watching}"로 일찍
끝낼 수 있습니다.
키: Y 보기, N 계속 플레이 또는 그만 보기. 컨트롤러: Y와 B(B를 길게 누르면 중지).
놓쳤다면 장면이 진행되는 동안 일시 정지 > "{B:Co-op}"에서 처음부터 볼 수 있습니다.

전투
전투는 한 플레이어의 것이며, 한 세계에서 동시에 하나만 진행됩니다. 누군가 싸우는
동안 다른 플레이어에게는 누가 싸우는지와 결과가 표시되고, 직접 전투를 시작할 수
없으며, 모두의 시간이 멈춥니다. 보상은 싸운 플레이어의 발밑에 떨어지고, 레벨
진행은 공유됩니다.

협동에서 아직 안 되는 것
함께 싸우기, 다른 플레이어가 대화에서 고른 대답(대사는 근처에 표시됨), 진행
중인 매장과 부검(무덤이나 테이블은 한 번에 한 명만 작업). 2~4인으로 테스트했습니다.
상인과는 한 번에 한 명만 거래하세요. 같은 순간에 두 거래가 이루어지면 마지막 물건을 둘 다 받을 수 있습니다.

제거
게임 폴더의 BepInEx\\plugins\\GK2Coop을 삭제합니다. BepInEx도 없애려면(다른 모드가
쓰지 않는 경우) BepInEx 폴더, winhttp.dll, doorstop_config.ini,
.doorstop_version, changelog.txt도 삭제합니다.
안내서 INSTALL-GK2COOP.txt와 GK2Coop-Guides도 삭제할 수 있습니다.

설정: BepInEx\\config\\com.fabio.gk2coop.cfg
개발 버전입니다. 소중한 저장 파일은 백업해 두세요.
"""

WORKSHOP['ko'] = """Graveyard Keeper 2 협동 - Steam 창작마당에서 설치

Steam은 이 모드를 내려받지만 켤 수는 없습니다. 한 번만 게임에 복사하면,
그 뒤로는 게임을 시작할 때 모드가 스스로 업데이트를 설치합니다.

1. 구독한 뒤 게임을 종료합니다.
2. 이 항목의 다운로드 폴더를 엽니다:
     <Steam 라이브러리>\\steamapps\\workshop\\content\\4358690\\<항목 ID>
   (Steam에서 게임을 오른쪽 클릭 > 관리 > 로컬 파일 보기를 연 다음, 두 단계
   위 폴더로 올라가 workshop\\content로 갑니다.)
3. 그 폴더의 모든 내용을 게임 폴더의 GraveyardKeeper2.exe 옆에 복사하고
   폴더 병합을 허용합니다.
4. 게임을 시작합니다. 메인 메뉴의 게임 버튼 아래에 "{B:Co-op}" 버튼이 생깁니다.

함께 플레이하는 모두가 같은 버전의 모드가 필요합니다. Steam이 항목을
업데이트하면 게임을 한 번 실행하세요. 메인 메뉴에 새 버전이 표시되고, 다음
실행부터 사용됩니다.

제거: 게임 폴더의 BepInEx\\plugins\\GK2Coop을 삭제합니다.
"""
