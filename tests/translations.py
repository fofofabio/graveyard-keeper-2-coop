# -*- coding: utf-8 -*-
"""The mod's translations, one row per English text, every language side by side.

Edit here, then run  python tests/Generate-Translations.py  to write src/GK2Coop/CoopText.<lang>.cs,
and  python tests/Check-Translations.py  to check nothing is missing. German lives in its own
hand-kept file (CoopText.de.cs) and is not generated.

Language ids are the game's own (LLBase.CurrentLang). Placeholders {0} {1} must stay.
"""

LANGS = {
    # id: (method name, file suffix)
    'fr': ('AddFrench', 'fr'),
    'es': ('AddSpanish', 'es'),
    'pt-br': ('AddPortuguese', 'pt-br'),
    'pl': ('AddPolish', 'pl'),
    'ru': ('AddRussian', 'ru'),
    'tr': ('AddTurkish', 'tr'),
    'ja': ('AddJapanese', 'ja'),
    'zh_cn': ('AddChinese', 'zh_cn'),
    'ko': ('AddKorean', 'ko'),
}

T = {}


def row(english, fr, es, pt, pl, ru, tr, ja, zh, ko):
    T[english] = {'fr': fr, 'es': es, 'pt-br': pt, 'pl': pl, 'ru': ru, 'tr': tr, 'ja': ja, 'zh_cn': zh, 'ko': ko}


# ---------------------------------------------------------------- main menu and the co-op window
row("Co-op",
    "Coop", "Cooperativo", "Co-op", "Co-op", "Кооператив", "Ortak oyun", "協力プレイ", "联机合作", "협동")
row("Play in one world together. One player hosts, the others join.",
    "Jouez ensemble dans un seul monde. L'un héberge, les autres rejoignent.",
    "Jugad juntos en un mismo mundo. Uno crea la partida y los demás se unen.",
    "Joguem juntos no mesmo mundo. Um hospeda, os outros entram.",
    "Grajcie razem w jednym świecie. Jedna osoba zakłada grę, reszta dołącza.",
    "Играйте вместе в одном мире. Один создаёт игру, остальные присоединяются.",
    "Aynı dünyada birlikte oynayın. Biri oyunu kurar, diğerleri katılır.",
    "ひとつの世界で一緒に遊びましょう。1人がホストし、ほかの人が参加します。",
    "在同一个世界里一起玩。一人创建游戏，其他人加入。",
    "한 세계에서 함께 플레이하세요. 한 명이 호스트하고 나머지가 참가합니다.")
row("Host a game",
    "Héberger une partie", "Crear partida", "Hospedar partida", "Załóż grę", "Создать игру", "Oyun kur",
    "ゲームをホスト", "创建游戏", "게임 호스트")
row("Join a game",
    "Rejoindre une partie", "Unirse a una partida", "Entrar em uma partida", "Dołącz do gry", "Присоединиться к игре", "Oyuna katıl",
    "ゲームに参加", "加入游戏", "게임 참가")
row("Cancel",
    "Annuler", "Cancelar", "Cancelar", "Anuluj", "Отмена", "İptal", "キャンセル", "取消", "취소")
row("Co-op is off. Your next game is single player.",
    "La coop est désactivée. Ta prochaine partie sera en solo.",
    "El cooperativo está desactivado. Tu próxima partida será individual.",
    "O co-op está desligado. Sua próxima partida será solo.",
    "Co-op wyłączony. Następna gra będzie jednoosobowa.",
    "Кооператив выключен. Следующая игра будет одиночной.",
    "Ortak oyun kapalı. Sonraki oyunun tek oyunculu olacak.",
    "協力プレイはオフです。次のゲームはシングルプレイになります。",
    "联机合作已关闭。下一局将是单人游戏。",
    "협동이 꺼졌습니다. 다음 게임은 싱글 플레이입니다.")
row("Your next game is hosted on Steam.",
    "Ta prochaine partie sera hébergée sur Steam.",
    "Tu próxima partida se creará en Steam.",
    "Sua próxima partida será hospedada na Steam.",
    "Następna gra zostanie założona przez Steam.",
    "Следующая игра будет создана через Steam.",
    "Sonraki oyunun Steam üzerinden kurulacak.",
    "次のゲームはSteamでホストします。",
    "下一局将通过 Steam 创建。",
    "다음 게임은 Steam으로 호스트합니다.")
row("Your next game is hosted on port {0}.",
    "Ta prochaine partie sera hébergée sur le port {0}.",
    "Tu próxima partida se creará en el puerto {0}.",
    "Sua próxima partida será hospedada na porta {0}.",
    "Następna gra zostanie założona na porcie {0}.",
    "Следующая игра будет создана на порту {0}.",
    "Sonraki oyunun {0} portunda kurulacak.",
    "次のゲームはポート{0}でホストします。",
    "下一局将在端口 {0} 上创建。",
    "다음 게임은 포트 {0}에서 호스트합니다.")
row("Your next game joins a Steam friend.",
    "Ta prochaine partie rejoindra un ami Steam.",
    "En tu próxima partida te unirás a un amigo de Steam.",
    "Na próxima partida você entra na de um amigo da Steam.",
    "W następnej grze dołączysz do znajomego ze Steam.",
    "В следующей игре ты присоединишься к другу из Steam.",
    "Sonraki oyununda bir Steam arkadaşına katılacaksın.",
    "次のゲームではSteamのフレンドに参加します。",
    "下一局将加入一位 Steam 好友。",
    "다음 게임에서는 Steam 친구에게 참가합니다.")
row("Your next game joins {0}.",
    "Ta prochaine partie rejoindra {0}.",
    "En tu próxima partida te unirás a {0}.",
    "Na próxima partida você entra em {0}.",
    "W następnej grze dołączysz do {0}.",
    "В следующей игре ты присоединишься к {0}.",
    "Sonraki oyununda {0} adresine katılacaksın.",
    "次のゲームでは{0}に参加します。",
    "下一局将加入 {0}。",
    "다음 게임에서는 {0}에 참가합니다.")
row("Your name",
    "Ton nom", "Tu nombre", "Seu nome", "Twoje imię", "Твоё имя", "Adın", "名前", "你的名字", "이름")
row("Players",
    "Joueurs", "Jugadores", "Jogadores", "Gracze", "Игроки", "Oyuncular", "人数", "玩家", "인원")
row("Host on Steam",
    "Héberger sur Steam", "Crear en Steam", "Hospedar na Steam", "Załóż przez Steam", "Создать через Steam", "Steam'de kur",
    "Steamでホスト", "通过 Steam 创建", "Steam으로 호스트")
row("Done. Load a game and your friends can join. Press {0} in game to invite them.",
    "C'est prêt. Charge une partie et tes amis pourront la rejoindre. Appuie sur {0} en jeu pour les inviter.",
    "Listo. Carga una partida y tus amigos podrán unirse. Pulsa {0} en el juego para invitarlos.",
    "Pronto. Carregue uma partida e seus amigos poderão entrar. Aperte {0} no jogo para convidá-los.",
    "Gotowe. Wczytaj grę, a znajomi będą mogli dołączyć. Naciśnij {0} w grze, aby ich zaprosić.",
    "Готово. Загрузи игру, и друзья смогут присоединиться. Нажми {0} в игре, чтобы пригласить их.",
    "Hazır. Bir oyun yükle, arkadaşların katılabilir. Onları davet etmek için oyunda {0} tuşuna bas.",
    "準備完了。ゲームをロードするとフレンドが参加できます。ゲーム中に{0}で招待できます。",
    "准备好了。载入游戏后好友就能加入。在游戏中按 {0} 邀请他们。",
    "준비됐습니다. 게임을 불러오면 친구들이 참가할 수 있습니다. 게임 중 {0} 키로 초대하세요.")
row("Friends join from their Steam friends list or through an invite.",
    "Tes amis rejoignent depuis leur liste d'amis Steam ou via une invitation.",
    "Tus amigos se unen desde su lista de amigos de Steam o con una invitación.",
    "Seus amigos entram pela lista de amigos da Steam ou por um convite.",
    "Znajomi dołączają z listy znajomych Steam albo przez zaproszenie.",
    "Друзья присоединяются через список друзей Steam или по приглашению.",
    "Arkadaşların Steam arkadaş listesinden ya da bir davetle katılır.",
    "フレンドはSteamのフレンドリストか招待から参加します。",
    "好友可以从 Steam 好友列表或邀请中加入。",
    "친구는 Steam 친구 목록이나 초대로 참가합니다.")
row("Host without Steam",
    "Héberger sans Steam", "Crear sin Steam", "Hospedar sem a Steam", "Załóż bez Steam", "Создать без Steam", "Steam olmadan kur",
    "Steamを使わずにホスト", "不通过 Steam 创建", "Steam 없이 호스트")
row("Others connect to this PC. That works on the same network, over a VPN such as Tailscale, or over the internet if your router forwards the port.",
    "Les autres se connectent à ce PC. Ça marche sur le même réseau, via un VPN comme Tailscale, ou par Internet si ta box redirige le port.",
    "Los demás se conectan a este PC. Funciona en la misma red, con una VPN como Tailscale o por internet si tu router redirige el puerto.",
    "Os outros se conectam a este PC. Funciona na mesma rede, por uma VPN como o Tailscale ou pela internet se o seu roteador redirecionar a porta.",
    "Pozostali łączą się z tym komputerem. Działa to w tej samej sieci, przez VPN taki jak Tailscale albo przez internet, jeśli router przekierowuje port.",
    "Остальные подключаются к этому ПК. Это работает в одной сети, через VPN вроде Tailscale или через интернет, если роутер перенаправляет порт.",
    "Diğerleri bu bilgisayara bağlanır. Aynı ağda, Tailscale gibi bir VPN ile ya da modemin portu yönlendiriyorsa internet üzerinden çalışır.",
    "ほかの人がこのPCに接続します。同じネットワーク内、TailscaleなどのVPN経由、またはルーターでポートを転送していればインターネット経由で使えます。",
    "其他人会连接到这台电脑。在同一网络中、通过 Tailscale 这类 VPN，或者在路由器转发了端口时通过互联网都可以。",
    "다른 사람들이 이 PC에 접속합니다. 같은 네트워크, Tailscale 같은 VPN, 또는 공유기에서 포트를 포워딩했다면 인터넷으로도 됩니다.")
row("Show my addresses",
    "Afficher mes adresses", "Mostrar mis direcciones", "Mostrar meus endereços", "Pokaż moje adresy", "Показать мои адреса", "Adreslerimi göster",
    "自分のアドレスを表示", "显示我的地址", "내 주소 보기")
row("Hide my addresses",
    "Masquer mes adresses", "Ocultar mis direcciones", "Ocultar meus endereços", "Ukryj moje adresy", "Скрыть мои адреса", "Adreslerimi gizle",
    "自分のアドレスを隠す", "隐藏我的地址", "내 주소 숨기기")
row("Copy",
    "Copier", "Copiar", "Copiar", "Kopiuj", "Копировать", "Kopyala", "コピー", "复制", "복사")
row("Address copied.",
    "Adresse copiée.", "Dirección copiada.", "Endereço copiado.", "Adres skopiowany.", "Адрес скопирован.", "Adres kopyalandı.",
    "アドレスをコピーしました。", "地址已复制。", "주소를 복사했습니다.")
row("Host",
    "Héberger", "Crear", "Hospedar", "Załóż", "Создать", "Kur", "ホスト", "创建", "호스트")
row("Done. Load a game, then give the others your address.",
    "C'est prêt. Charge une partie, puis donne ton adresse aux autres.",
    "Listo. Carga una partida y pasa tu dirección a los demás.",
    "Pronto. Carregue uma partida e passe seu endereço para os outros.",
    "Gotowe. Wczytaj grę i podaj pozostałym swój adres.",
    "Готово. Загрузи игру и дай остальным свой адрес.",
    "Hazır. Bir oyun yükle, sonra adresini diğerlerine ver.",
    "準備完了。ゲームをロードして、ほかの人にアドレスを伝えてください。",
    "准备好了。载入游戏，然后把你的地址告诉其他人。",
    "준비됐습니다. 게임을 불러온 뒤 다른 사람들에게 주소를 알려 주세요.")
row("Home network",
    "Réseau local", "Red local", "Rede local", "Sieć domowa", "Домашняя сеть", "Ev ağı", "ホームネットワーク", "局域网", "홈 네트워크")
row("No usable address found.",
    "Aucune adresse utilisable.", "No hay ninguna dirección utilizable.", "Nenhum endereço utilizável.", "Brak adresu, którego można użyć.",
    "Подходящий адрес не найден.", "Kullanılabilir adres bulunamadı.", "使えるアドレスが見つかりません。", "没有可用的地址。", "사용할 수 있는 주소가 없습니다.")
row("Friends hosting now",
    "Amis qui hébergent", "Amigos con partida abierta", "Amigos hospedando agora", "Znajomi z otwartą grą", "Друзья, создавшие игру",
    "Şu an oyun kuran arkadaşlar", "ホスト中のフレンド", "正在开房的好友", "호스트 중인 친구")
row("None right now. You can also accept an invite in Steam.",
    "Personne pour l'instant. Tu peux aussi accepter une invitation dans Steam.",
    "Nadie por ahora. También puedes aceptar una invitación en Steam.",
    "Ninguém no momento. Você também pode aceitar um convite na Steam.",
    "Na razie nikt. Możesz też przyjąć zaproszenie na Steamie.",
    "Пока никого. Можно также принять приглашение в Steam.",
    "Şu an kimse yok. Steam'de bir daveti de kabul edebilirsin.",
    "今はいません。Steamで招待を受けることもできます。",
    "暂时没有。你也可以在 Steam 中接受邀请。",
    "지금은 없습니다. Steam에서 초대를 수락할 수도 있습니다.")
row("Join",
    "Rejoindre", "Unirse", "Entrar", "Dołącz", "Присоединиться", "Katıl", "参加", "加入", "참가")
row("Join by address",
    "Rejoindre par adresse", "Unirse por dirección", "Entrar pelo endereço", "Dołącz przez adres", "Присоединиться по адресу", "Adresle katıl",
    "アドレスで参加", "通过地址加入", "주소로 참가")
row("Address",
    "Adresse", "Dirección", "Endereço", "Adres", "Адрес", "Adres", "アドレス", "地址", "주소")
row("Enter the host's address first.",
    "Entre d'abord l'adresse de l'hôte.", "Primero escribe la dirección del anfitrión.", "Digite primeiro o endereço do anfitrião.",
    "Najpierw wpisz adres gospodarza.", "Сначала введи адрес хоста.", "Önce kurucunun adresini gir.",
    "先にホストのアドレスを入力してください。", "请先输入房主的地址。", "먼저 호스트의 주소를 입력하세요.")
row("You play in a copy of the host's world. Your own saves are not touched.",
    "Tu joues dans une copie du monde de l'hôte. Tes propres sauvegardes ne sont pas touchées.",
    "Juegas en una copia del mundo del anfitrión. Tus partidas guardadas no se tocan.",
    "Você joga em uma cópia do mundo do anfitrião. Seus próprios saves não são alterados.",
    "Grasz w kopii świata gospodarza. Twoje własne zapisy pozostają nietknięte.",
    "Ты играешь в копии мира хоста. Твои собственные сохранения не затрагиваются.",
    "Kurucunun dünyasının bir kopyasında oynarsın. Kendi kayıtlarına dokunulmaz.",
    "ホストの世界のコピーで遊びます。自分のセーブデータはそのままです。",
    "你会在房主世界的副本中游玩。你自己的存档不会被改动。",
    "호스트 세계의 사본에서 플레이합니다. 내 저장 파일은 그대로입니다.")
row("Join without copying",
    "Rejoindre sans copier", "Unirse sin copiar", "Entrar sem copiar", "Dołącz bez kopiowania", "Присоединиться без копирования", "Kopyalamadan katıl",
    "コピーせずに参加", "不复制直接加入", "복사 없이 참가")
row("Only when you both have the same save.",
    "Seulement si vous avez tous les deux la même sauvegarde.", "Solo si los dos tenéis la misma partida guardada.",
    "Só quando vocês dois têm o mesmo save.", "Tylko gdy oboje macie ten sam zapis.", "Только если у вас обоих одно и то же сохранение.",
    "Sadece ikiniz de aynı kayda sahipseniz.", "2人が同じセーブデータを持っている場合のみ。", "仅当你们有相同的存档时。", "둘 다 같은 저장 파일이 있을 때만.")
row("Done. Load the same save as the host to join {0}.",
    "C'est prêt. Charge la même sauvegarde que l'hôte pour rejoindre {0}.",
    "Listo. Carga la misma partida que el anfitrión para unirte a {0}.",
    "Pronto. Carregue o mesmo save do anfitrião para entrar em {0}.",
    "Gotowe. Wczytaj ten sam zapis co gospodarz, aby dołączyć do {0}.",
    "Готово. Загрузи то же сохранение, что и хост, чтобы присоединиться к {0}.",
    "Hazır. {0} adresine katılmak için kurucuyla aynı kaydı yükle.",
    "準備完了。{0}に参加するには、ホストと同じセーブデータをロードしてください。",
    "准备好了。载入和房主相同的存档即可加入 {0}。",
    "준비됐습니다. {0}에 참가하려면 호스트와 같은 저장 파일을 불러오세요.")
row("Watch {0}'s scene",
    "Regarder la scène de {0}", "Ver la escena de {0}", "Assistir à cena de {0}", "Obejrzyj scenę: {0}", "Смотреть сцену: {0}", "{0} adlı oyuncunun sahnesini izle", "{0}のシーンを見る", "观看 {0} 的剧情", "{0} 님의 장면 보기")
row("Quick message",
    "Message rapide", "Mensaje rápido", "Mensagem rápida", "Szybka wiadomość", "Быстрое сообщение", "Hızlı mesaj", "クイックメッセージ", "快捷消息", "빠른 메시지")
row("Come here!", "Viens ici !", "¡Ven aquí!", "Vem aqui!", "Chodź tutaj!", "Иди сюда!", "Buraya gel!", "こっちに来て！", "过来！", "이리 와!")
row("Look at this!", "Regarde ça !", "¡Mira esto!", "Olha isso!", "Spójrz na to!", "Посмотри!", "Şuna bak!", "これを見て！", "看这个！", "이것 좀 봐!")
row("Wait for me!", "Attends-moi !", "¡Espérame!", "Me espera!", "Zaczekaj na mnie!", "Подожди меня!", "Beni bekle!", "待って！", "等等我！", "기다려 줘!")
row("Thanks!", "Merci !", "¡Gracias!", "Obrigado!", "Dzięki!", "Спасибо!", "Teşekkürler!", "ありがとう！", "谢谢！", "고마워!")
row("Yes.", "Oui.", "Sí.", "Sim.", "Tak.", "Да.", "Evet.", "はい。", "好的。", "응.")
row("No.", "Non.", "No.", "Não.", "Nie.", "Нет.", "Hayır.", "いいえ。", "不。", "아니.")
row("I'm going to sleep.", "Je vais dormir.", "Me voy a dormir.", "Vou dormir.", "Idę spać.", "Я иду спать.", "Uyumaya gidiyorum.", "寝るね。", "我去睡觉了。", "자러 갈게.")
row("Write a message",
    "Écrire un message", "Escribir un mensaje", "Escrever uma mensagem", "Napisz wiadomość", "Написать сообщение", "Mesaj yaz", "メッセージを書く", "发送消息", "메시지 쓰기")
row("Invite friends",
    "Inviter des amis", "Invitar amigos", "Convidar amigos", "Zaproś znajomych", "Пригласить друзей", "Arkadaş davet et", "フレンドを招待", "邀请好友", "친구 초대")
row("Change your look",
    "Changer d'apparence", "Cambiar tu aspecto", "Mudar sua aparência", "Zmień wygląd", "Изменить внешность", "Görünümünü değiştir", "見た目を変える", "更改外观", "외형 변경")
row("Show name tags",
    "Afficher les noms", "Mostrar nombres", "Mostrar nomes", "Pokaż imiona", "Показать имена", "İsimleri göster", "名前を表示", "显示名字", "이름표 표시")
row("Hide name tags",
    "Masquer les noms", "Ocultar nombres", "Ocultar nomes", "Ukryj imiona", "Скрыть имена", "İsimleri gizle", "名前を隠す", "隐藏名字", "이름표 숨기기")
row("Show the status",
    "Afficher le statut", "Mostrar el estado", "Mostrar o status", "Pokaż stan", "Показать статус", "Durumu göster", "ステータスを表示", "显示状态", "상태 표시")
row("Hide the status",
    "Masquer le statut", "Ocultar el estado", "Ocultar o status", "Ukryj stan", "Скрыть статус", "Durumu gizle", "ステータスを隠す", "隐藏状态", "상태 숨기기")
row("Type",
    "Saisie", "Escribir", "Digitar", "Wpisz", "Ввод", "Yaz", "入力", "输入", "입력")
row("Space",
    "Espace", "Espacio", "Espaço", "Spacja", "Пробел", "Boşluk", "スペース", "空格", "띄어쓰기")
row("{0} is fighting.",
    "{0} est en plein combat.", "{0} está luchando.", "{0} está lutando.", "{0} walczy.",
    "{0} сражается.", "{0} savaşıyor.", "{0}が戦闘中です。", "{0}正在战斗。", "{0} 님이 전투 중입니다.")
row("{0} won the fight.",
    "{0} a gagné le combat.", "{0} ganó el combate.", "{0} venceu a luta.", "{0} wygrywa walkę.",
    "{0} победил в бою.", "{0} savaşı kazandı.", "{0}が戦闘に勝利しました。", "{0}赢得了战斗。", "{0} 님이 전투에서 이겼습니다.")
row("{0} lost the fight.",
    "{0} a perdu le combat.", "{0} perdió el combate.", "{0} perdeu a luta.", "{0} przegrywa walkę.",
    "{0} проиграл бой.", "{0} savaşı kaybetti.", "{0}が戦闘に敗れました。", "{0}输掉了战斗。", "{0} 님이 전투에서 졌습니다.")
row("{0}'s fight is over.",
    "Le combat de {0} est terminé.", "El combate de {0} ha terminado.", "A luta de {0} acabou.", "Walka gracza {0} dobiegła końca.",
    "Бой игрока {0} окончен.", "{0} adlı oyuncunun savaşı bitti.", "{0}の戦闘が終わりました。", "{0}的战斗结束了。", "{0} 님의 전투가 끝났습니다.")
row("{0} is working here.",
    "{0} travaille ici.", "{0} está trabajando aquí.", "{0} está trabalhando aqui.", "{0} tu teraz pracuje.",
    "Здесь работает {0}.", "{0} burada çalışıyor.", "{0}がここで作業中です。", "{0}正在这里工作。", "{0} 님이 여기서 작업 중입니다."),
row("Delete",
    "Effacer", "Borrar", "Apagar", "Usuń", "Стереть", "Sil", "削除", "删除", "지우기")
row("Recent",
    "Récents", "Recientes", "Recentes", "Ostatnie", "Недавние", "Son kullanılanlar", "最近", "最近", "최근")
row("Select",
    "Choisir", "Elegir", "Selecionar", "Wybierz", "Выбрать", "Seç", "選択", "选择", "선택")
row("Back",
    "Retour", "Atrás", "Voltar", "Wstecz", "Назад", "Geri", "戻る", "返回", "뒤로")
row("Close",
    "Fermer", "Cerrar", "Fechar", "Zamknij", "Закрыть", "Kapat", "閉じる", "关闭", "닫기")

# ---------------------------------------------------------------- joining: copying the host's world
row("Copying the host's world…",
    "Copie du monde de l'hôte…", "Copiando el mundo del anfitrión…", "Copiando o mundo do anfitrião…", "Kopiowanie świata gospodarza…",
    "Копирование мира хоста…", "Kurucunun dünyası kopyalanıyor…", "ホストの世界をコピー中…", "正在复制房主的世界…", "호스트의 세계를 복사하는 중…")
row("Could not copy the host's world.",
    "Impossible de copier le monde de l'hôte.", "No se pudo copiar el mundo del anfitrión.", "Não foi possível copiar o mundo do anfitrião.",
    "Nie udało się skopiować świata gospodarza.", "Не удалось скопировать мир хоста.", "Kurucunun dünyası kopyalanamadı.",
    "ホストの世界をコピーできませんでした。", "无法复制房主的世界。", "호스트의 세계를 복사하지 못했습니다.")
row("Connecting to {0}…",
    "Connexion à {0}…", "Conectando con {0}…", "Conectando a {0}…", "Łączenie z {0}…", "Подключение к {0}…", "{0} adresine bağlanılıyor…",
    "{0}に接続中…", "正在连接 {0}…", "{0}에 연결하는 중…")
row("Copying the host's world ({0}%)…",
    "Copie du monde de l'hôte ({0} %)…", "Copiando el mundo del anfitrión ({0} %)…", "Copiando o mundo do anfitrião ({0}%)…",
    "Kopiowanie świata gospodarza ({0}%)…", "Копирование мира хоста ({0}%)…", "Kurucunun dünyası kopyalanıyor (%{0})…",
    "ホストの世界をコピー中（{0}%）…", "正在复制房主的世界（{0}%）…", "호스트의 세계를 복사하는 중 ({0}%)…")
row("World copied. Loading it…",
    "Monde copié. Chargement…", "Mundo copiado. Cargando…", "Mundo copiado. Carregando…", "Świat skopiowany. Wczytywanie…",
    "Мир скопирован. Загрузка…", "Dünya kopyalandı. Yükleniyor…", "コピー完了。ロード中…", "世界已复制，正在载入…", "복사 완료. 불러오는 중…")
row("Could not start a connection to {0}.",
    "Impossible de lancer la connexion à {0}.", "No se pudo iniciar la conexión con {0}.", "Não foi possível iniciar a conexão com {0}.",
    "Nie udało się rozpocząć połączenia z {0}.", "Не удалось начать подключение к {0}.", "{0} bağlantısı başlatılamadı.",
    "{0}への接続を開始できませんでした。", "无法开始连接 {0}。", "{0}에 연결을 시작하지 못했습니다.")
row("Could not reach the host at {0}. Make sure they are hosting and already in their world. Over the internet, their router has to forward port {1}.",
    "Impossible de joindre l'hôte à {0}. Vérifie qu'il héberge et qu'il est déjà dans son monde. Par Internet, sa box doit rediriger le port {1}.",
    "No se pudo contactar con el anfitrión en {0}. Asegúrate de que tenga la partida creada y ya esté en su mundo. Por internet, su router debe redirigir el puerto {1}.",
    "Não foi possível alcançar o anfitrião em {0}. Confira se ele está hospedando e já está no mundo dele. Pela internet, o roteador dele precisa redirecionar a porta {1}.",
    "Nie można połączyć się z gospodarzem pod {0}. Upewnij się, że założył grę i jest już w swoim świecie. Przez internet jego router musi przekierować port {1}.",
    "Не удалось связаться с хостом по адресу {0}. Убедись, что он создал игру и уже находится в своём мире. Через интернет его роутер должен перенаправлять порт {1}.",
    "{0} adresindeki kurucuya ulaşılamadı. Oyunu kurduğundan ve dünyasında olduğundan emin ol. İnternet üzerinden modeminin {1} portunu yönlendirmesi gerekir.",
    "{0}のホストに接続できませんでした。ホストしていて、すでに自分の世界にいるか確認してください。インターネット経由の場合は、ホストのルーターでポート{1}を転送する必要があります。",
    "无法连接到 {0} 的房主。请确认对方已创建游戏并已进入自己的世界。通过互联网连接时，对方的路由器需要转发端口 {1}。",
    "{0}의 호스트에 연결할 수 없습니다. 호스트하고 있고 이미 자기 세계에 들어가 있는지 확인하세요. 인터넷으로는 호스트의 공유기가 포트 {1}을(를) 포워딩해야 합니다.")
row("The host stopped sending the world. Check the connection and try again.",
    "L'hôte a cessé d'envoyer le monde. Vérifie la connexion et réessaie.",
    "El anfitrión dejó de enviar el mundo. Revisa la conexión e inténtalo de nuevo.",
    "O anfitrião parou de enviar o mundo. Verifique a conexão e tente de novo.",
    "Gospodarz przestał wysyłać świat. Sprawdź połączenie i spróbuj ponownie.",
    "Хост перестал отправлять мир. Проверь подключение и попробуй снова.",
    "Kurucu dünyayı göndermeyi bıraktı. Bağlantını kontrol edip tekrar dene.",
    "ホストからの世界の送信が止まりました。接続を確認してもう一度試してください。",
    "房主停止了发送世界。请检查连接后重试。",
    "호스트가 세계 전송을 멈췄습니다. 연결을 확인하고 다시 시도하세요.")
row("Could not read the host's world: {0}",
    "Impossible de lire le monde de l'hôte : {0}", "No se pudo leer el mundo del anfitrión: {0}", "Não foi possível ler o mundo do anfitrião: {0}",
    "Nie udało się odczytać świata gospodarza: {0}", "Не удалось прочитать мир хоста: {0}", "Kurucunun dünyası okunamadı: {0}",
    "ホストの世界を読み込めませんでした：{0}", "无法读取房主的世界：{0}", "호스트의 세계를 읽지 못했습니다: {0}")
row("Could not import the host's world: {0}",
    "Impossible d'importer le monde de l'hôte : {0}", "No se pudo importar el mundo del anfitrión: {0}", "Não foi possível importar o mundo do anfitrião: {0}",
    "Nie udało się zaimportować świata gospodarza: {0}", "Не удалось импортировать мир хоста: {0}", "Kurucunun dünyası içe aktarılamadı: {0}",
    "ホストの世界を取り込めませんでした：{0}", "无法导入房主的世界：{0}", "호스트의 세계를 가져오지 못했습니다: {0}")
row("Could not load the host's world: {0}",
    "Impossible de charger le monde de l'hôte : {0}", "No se pudo cargar el mundo del anfitrión: {0}", "Não foi possível carregar o mundo do anfitrião: {0}",
    "Nie udało się wczytać świata gospodarza: {0}", "Не удалось загрузить мир хоста: {0}", "Kurucunun dünyası yüklenemedi: {0}",
    "ホストの世界をロードできませんでした：{0}", "无法载入房主的世界：{0}", "호스트의 세계를 불러오지 못했습니다: {0}")

# ---------------------------------------------------------------- session status
row("Connecting…",
    "Connexion…", "Conectando…", "Conectando…", "Łączenie…", "Подключение…", "Bağlanılıyor…", "接続中…", "正在连接…", "연결하는 중…")
row("Could not connect",
    "Connexion impossible", "No se pudo conectar", "Não foi possível conectar", "Nie udało się połączyć", "Не удалось подключиться", "Bağlanılamadı",
    "接続できませんでした", "无法连接", "연결하지 못했습니다")
row("Connection refused",
    "Connexion refusée", "Conexión rechazada", "Conexão recusada", "Połączenie odrzucone", "Подключение отклонено", "Bağlantı reddedildi",
    "接続を拒否されました", "连接被拒绝", "연결이 거부되었습니다")
row("Hosting",
    "Tu héberges", "Partida creada", "Hospedando", "Twoja gra", "Ты хост", "Oyunu kurdun", "ホスト中", "你是房主", "호스트 중")
row("Waiting for players…",
    "En attente de joueurs…", "Esperando jugadores…", "Aguardando jogadores…", "Czekanie na graczy…", "Ожидание игроков…", "Oyuncular bekleniyor…",
    "プレイヤーを待っています…", "等待玩家加入…", "플레이어를 기다리는 중…")
row("With {0}",
    "Avec {0}", "Con {0}", "Com {0}", "Gracze: {0}", "Игроки: {0}", "Oyuncular: {0}", "参加者：{0}", "玩家：{0}", "참가자: {0}")
row("In {0}'s world",
    "Dans le monde de {0}", "En el mundo de {0}", "No mundo de {0}", "W świecie gracza {0}", "В мире игрока {0}", "{0} adlı oyuncunun dünyasında",
    "{0}の世界", "在 {0} 的世界", "{0}의 세계")

# ---------------------------------------------------------------- announcements
row("{0} joined.",
    "{0} a rejoint la partie.", "{0} se ha unido.", "{0} entrou.", "{0} dołączył(a).", "{0} присоединяется.", "{0} katıldı.",
    "{0}が参加しました。", "{0} 加入了游戏。", "{0} 님이 참가했습니다.")
row("{0} left.",
    "{0} a quitté la partie.", "{0} se ha ido.", "{0} saiu.", "{0} opuścił(a) grę.", "{0} выходит из игры.", "{0} ayrıldı.",
    "{0}が退出しました。", "{0} 离开了游戏。", "{0} 님이 나갔습니다.")
row("{0} could not join: they have co-op mod {1}, you have {2}. You both need the same version.",
    "{0} n'a pas pu rejoindre : il a le mod coop {1}, tu as {2}. Il vous faut la même version.",
    "{0} no pudo unirse: tiene el mod cooperativo {1} y tú el {2}. Necesitáis la misma versión.",
    "{0} não conseguiu entrar: tem o mod co-op {1} e você tem {2}. Vocês precisam da mesma versão.",
    "{0} nie może dołączyć: ma mod co-op {1}, a ty {2}. Potrzebujecie tej samej wersji.",
    "{0} не может присоединиться: у него мод кооператива {1}, у тебя {2}. Нужна одна и та же версия.",
    "{0} katılamadı: onda ortak oyun modu {1}, sende {2} var. İkinizde de aynı sürüm olmalı.",
    "{0}は参加できませんでした。相手の協力プレイMODは{1}、あなたは{2}です。同じバージョンが必要です。",
    "{0} 无法加入：对方的联机模组是 {1}，你的是 {2}。你们需要相同的版本。",
    "{0} 님은 참가할 수 없습니다. 상대의 협동 모드는 {1}, 내 모드는 {2}입니다. 같은 버전이 필요합니다.")
row("You joined {0}'s game.",
    "Tu as rejoint la partie de {0}.", "Te has unido a la partida de {0}.", "Você entrou na partida de {0}.", "Dołączono do gry gracza {0}.",
    "Подключено к игре {0}.", "{0} adlı oyuncunun oyununa katıldın.", "{0}のゲームに参加しました。", "你加入了 {0} 的游戏。", "{0}의 게임에 참가했습니다.")
row("The host turned you away. {0}",
    "L'hôte a refusé ta connexion. {0}", "El anfitrión rechazó tu conexión. {0}", "O anfitrião recusou sua conexão. {0}",
    "Gospodarz odrzucił połączenie. {0}", "Хост отклонил подключение. {0}", "Kurucu bağlantını reddetti. {0}",
    "ホストに接続を断られました。{0}", "房主拒绝了你的连接。{0}", "호스트가 연결을 거부했습니다. {0}")
row("The host has a different version of the co-op mod. You both need the same version.",
    "L'hôte a une autre version du mod coop. Il vous faut la même version.",
    "El anfitrión tiene otra versión del mod cooperativo. Necesitáis la misma versión.",
    "O anfitrião tem outra versão do mod co-op. Vocês precisam da mesma versão.",
    "Gospodarz ma inną wersję moda co-op. Potrzebujecie tej samej wersji.",
    "У хоста другая версия мода кооператива. Нужна одна и та же версия.",
    "Kurucuda ortak oyun modunun farklı bir sürümü var. İkinizde de aynı sürüm olmalı.",
    "ホストの協力プレイMODのバージョンが違います。同じバージョンが必要です。",
    "房主的联机模组版本不同。你们需要相同的版本。",
    "호스트의 협동 모드 버전이 다릅니다. 같은 버전이 필요합니다.")
row("Could not join. {0}",
    "Impossible de rejoindre. {0}", "No se pudo unir. {0}", "Não foi possível entrar. {0}", "Nie udało się dołączyć. {0}",
    "Не удалось присоединиться. {0}", "Katılınamadı. {0}", "参加できませんでした。{0}", "无法加入。{0}", "참가하지 못했습니다. {0}")
row("Could not reach {0}:{1}. Make sure the host is hosting. Over the internet, port {1} has to be forwarded to their PC.",
    "Impossible de joindre {0}:{1}. Vérifie que l'hôte héberge. Par Internet, le port {1} doit être redirigé vers son PC.",
    "No se pudo contactar con {0}:{1}. Asegúrate de que el anfitrión tenga la partida creada. Por internet, el puerto {1} debe redirigirse a su PC.",
    "Não foi possível alcançar {0}:{1}. Confira se o anfitrião está hospedando. Pela internet, a porta {1} precisa ser redirecionada para o PC dele.",
    "Nie można połączyć się z {0}:{1}. Upewnij się, że gospodarz założył grę. Przez internet port {1} musi być przekierowany na jego komputer.",
    "Не удалось связаться с {0}:{1}. Убедись, что хост создал игру. Через интернет порт {1} должен быть перенаправлен на его ПК.",
    "{0}:{1} adresine ulaşılamadı. Kurucunun oyunu kurduğundan emin ol. İnternet üzerinden {1} portu onun bilgisayarına yönlendirilmelidir.",
    "{0}:{1}に接続できませんでした。ホストしているか確認してください。インターネット経由の場合は、ポート{1}をホストのPCへ転送する必要があります。",
    "无法连接到 {0}:{1}。请确认房主已创建游戏。通过互联网连接时，端口 {1} 需要转发到对方的电脑。",
    "{0}:{1}에 연결할 수 없습니다. 호스트하고 있는지 확인하세요. 인터넷으로는 포트 {1}을(를) 호스트의 PC로 포워딩해야 합니다.")
row("No reply from the host. Trying again ({0} of {1})…",
    "Pas de réponse de l'hôte. Nouvel essai ({0} sur {1})…", "El anfitrión no responde. Reintentando ({0} de {1})…",
    "Sem resposta do anfitrião. Tentando de novo ({0} de {1})…", "Brak odpowiedzi od gospodarza. Kolejna próba ({0} z {1})…",
    "Хост не отвечает. Повторная попытка ({0} из {1})…", "Kurucudan yanıt yok. Yeniden deneniyor ({0}/{1})…",
    "ホストから応答がありません。再試行中（{0}/{1}）…", "房主没有回应。正在重试（{0}/{1}）…", "호스트의 응답이 없습니다. 다시 시도하는 중 ({0}/{1})…")
row("Welcome back. Your inventory and progress are restored.",
    "Bon retour. Ton inventaire et ta progression sont restaurés.", "Bienvenido de nuevo. Tu inventario y tu progreso se han restaurado.",
    "Bem-vindo de volta. Seu inventário e seu progresso foram restaurados.", "Witaj z powrotem. Ekwipunek i postępy zostały przywrócone.",
    "С возвращением. Инвентарь и прогресс восстановлены.", "Tekrar hoş geldin. Envanterin ve ilerlemen geri yüklendi.",
    "おかえりなさい。インベントリと進行状況を復元しました。", "欢迎回来。你的物品栏和进度已恢复。", "다시 오신 것을 환영합니다. 인벤토리와 진행 상황을 복원했습니다.")
row("You start with a fresh inventory.",
    "Tu commences avec un inventaire vide.", "Empiezas con un inventario nuevo.", "Você começa com um inventário novo.", "Zaczynasz z nowym ekwipunkiem.",
    "Ты начинаешь с новым инвентарём.", "Yeni bir envanterle başlıyorsun.", "新しいインベントリで始めます。", "你将使用全新的物品栏开始。", "새 인벤토리로 시작합니다.")
row("Your inventory could not be restored. You have the host's copy for now.",
    "Ton inventaire n'a pas pu être restauré. Tu as la copie de l'hôte pour l'instant.",
    "No se pudo restaurar tu inventario. Por ahora tienes la copia del anfitrión.",
    "Não foi possível restaurar seu inventário. Por enquanto você está com a cópia do anfitrião.",
    "Nie udało się przywrócić ekwipunku. Na razie masz kopię gospodarza.",
    "Не удалось восстановить инвентарь. Пока у тебя копия хоста.",
    "Envanterin geri yüklenemedi. Şimdilik kurucunun kopyası sende.",
    "インベントリを復元できませんでした。今はホストのコピーを使っています。",
    "无法恢复你的物品栏。暂时使用房主的副本。",
    "인벤토리를 복원하지 못했습니다. 지금은 호스트의 사본을 사용합니다.")
row("Everyone is asleep. The night passes.",
    "Tout le monde dort. La nuit passe.", "Todos duermen. La noche pasa.", "Todos estão dormindo. A noite passa.", "Wszyscy śpią. Noc mija.",
    "Все спят. Ночь проходит.", "Herkes uyuyor. Gece geçiyor.", "全員が眠りました。夜が明けます。", "所有人都睡着了，夜晚过去了。", "모두 잠들었습니다. 밤이 지나갑니다.")
row("Waiting for everyone to sleep.",
    "En attente que tout le monde dorme.", "Esperando a que todos duerman.", "Aguardando todos dormirem.", "Czekanie, aż wszyscy zasną.",
    "Ждём, пока все уснут.", "Herkesin uyuması bekleniyor.", "全員が眠るのを待っています。", "等待所有人入睡。", "모두 잠들기를 기다리는 중입니다.")
row("You sleep and recover. The night only passes when everyone sleeps.",
    "Tu dors et récupères. La nuit ne passe que quand tout le monde dort.",
    "Duermes y te recuperas. La noche solo pasa cuando todos duermen.",
    "Você dorme e se recupera. A noite só passa quando todos dormem.",
    "Śpisz i odpoczywasz. Noc mija dopiero, gdy wszyscy śpią.",
    "Ты спишь и восстанавливаешься. Ночь пройдёт, только когда уснут все.",
    "Uyuyor ve dinleniyorsun. Gece ancak herkes uyuyunca geçer.",
    "眠って回復します。夜は全員が眠ったときだけ明けます。",
    "你在睡觉恢复体力。只有所有人都睡着，夜晚才会过去。",
    "잠을 자며 회복합니다. 밤은 모두가 잠들어야 지나갑니다.")
row("A friend invited you. Go back to the main menu to join.",
    "Un ami t'a invité. Retourne au menu principal pour le rejoindre.", "Un amigo te ha invitado. Vuelve al menú principal para unirte.",
    "Um amigo convidou você. Volte ao menu principal para entrar.", "Znajomy cię zaprosił. Wróć do menu głównego, aby dołączyć.",
    "Друг пригласил тебя. Вернись в главное меню, чтобы присоединиться.", "Bir arkadaşın seni davet etti. Katılmak için ana menüye dön.",
    "フレンドから招待されました。参加するにはメインメニューに戻ってください。", "好友邀请了你。返回主菜单即可加入。", "친구가 초대했습니다. 참가하려면 메인 메뉴로 돌아가세요.")
row("Joining {0}…",
    "Connexion à la partie de {0}…", "Uniéndose a {0}…", "Entrando na partida de {0}…", "Dołączanie do {0}…", "Присоединение к {0}…",
    "{0} adlı oyuncuya katılınıyor…", "{0}に参加中…", "正在加入 {0}…", "{0}에 참가하는 중…")
row("Joining your friend…",
    "Connexion à la partie de ton ami…", "Uniéndose a tu amigo…", "Entrando na partida do seu amigo…", "Dołączanie do znajomego…",
    "Присоединение к другу…", "Arkadaşına katılınıyor…", "フレンドに参加中…", "正在加入好友…", "친구에게 참가하는 중…")
row("Could not enter your friend's game ({0}).",
    "Impossible d'entrer dans la partie de ton ami ({0}).", "No se pudo entrar en la partida de tu amigo ({0}).",
    "Não foi possível entrar na partida do seu amigo ({0}).", "Nie udało się wejść do gry znajomego ({0}).",
    "Не удалось войти в игру друга ({0}).", "Arkadaşının oyununa girilemedi ({0}).",
    "フレンドのゲームに入れませんでした（{0}）。", "无法进入好友的游戏（{0}）。", "친구의 게임에 들어가지 못했습니다 ({0}).")
row("That Steam game is not a co-op session.",
    "Cette partie Steam n'est pas une session coop.", "Esa partida de Steam no es una sesión cooperativa.",
    "Essa partida da Steam não é uma sessão co-op.", "Ta gra na Steamie nie jest sesją co-op.",
    "Эта игра в Steam не является сессией кооператива.", "O Steam oyunu bir ortak oyun oturumu değil.",
    "そのSteamのゲームは協力プレイのセッションではありません。", "这个 Steam 游戏不是联机合作会话。", "그 Steam 게임은 협동 세션이 아닙니다.")
row("Your friend's co-op mod does not match yours. You both need the same release.",
    "Le mod coop de ton ami ne correspond pas au tien. Il vous faut la même version.",
    "El mod cooperativo de tu amigo no coincide con el tuyo. Necesitáis la misma versión.",
    "O mod co-op do seu amigo não é igual ao seu. Vocês precisam da mesma versão.",
    "Mod co-op znajomego nie pasuje do twojego. Potrzebujecie tej samej wersji.",
    "Мод кооператива у друга не совпадает с твоим. Нужна одна и та же версия.",
    "Arkadaşının ortak oyun modu seninkiyle uyuşmuyor. İkinizde de aynı sürüm olmalı.",
    "フレンドの協力プレイMODがあなたのものと合いません。同じバージョンが必要です。",
    "好友的联机模组与你的不匹配。你们需要相同的版本。",
    "친구의 협동 모드가 내 것과 맞지 않습니다. 같은 버전이 필요합니다.")
row("Your friend has co-op mod {0}, you have {1}. You both need the same version.",
    "Ton ami a le mod coop {0}, tu as {1}. Il vous faut la même version.",
    "Tu amigo tiene el mod cooperativo {0} y tú el {1}. Necesitáis la misma versión.",
    "Seu amigo tem o mod co-op {0} e você tem {1}. Vocês precisam da mesma versão.",
    "Znajomy ma mod co-op {0}, a ty {1}. Potrzebujecie tej samej wersji.",
    "У друга мод кооператива {0}, у тебя {1}. Нужна одна и та же версия.",
    "Arkadaşında ortak oyun modu {0}, sende {1} var. İkinizde de aynı sürüm olmalı.",
    "フレンドの協力プレイMODは{0}、あなたは{1}です。同じバージョンが必要です。",
    "好友的联机模组是 {0}，你的是 {1}。你们需要相同的版本。",
    "친구의 협동 모드는 {0}, 내 모드는 {1}입니다. 같은 버전이 필요합니다.")
row("Inviting works while you host on Steam.",
    "L'invitation fonctionne quand tu héberges sur Steam.", "Solo puedes invitar mientras tienes una partida creada en Steam.",
    "Convidar funciona enquanto você hospeda na Steam.", "Zapraszać można, gdy grę założono przez Steam.",
    "Приглашать можно, когда игра создана через Steam.", "Davet etmek, Steam'de oyun kurduğunda çalışır.",
    "招待はSteamでホストしているときに使えます。", "只有通过 Steam 创建游戏时才能邀请。", "초대는 Steam으로 호스트할 때 사용할 수 있습니다.")
row("Co-op mod {0} was installed from the Workshop. Restart the game to use it.",
    "Le mod coop {0} a été installé depuis le Workshop. Relance le jeu pour l'utiliser.",
    "El mod cooperativo {0} se instaló desde el Workshop. Reinicia el juego para usarlo.",
    "O mod co-op {0} foi instalado pelo Workshop. Reinicie o jogo para usá-lo.",
    "Mod co-op {0} został zainstalowany z Warsztatu. Uruchom grę ponownie, aby go użyć.",
    "Мод кооператива {0} установлен из Мастерской. Перезапусти игру, чтобы им воспользоваться.",
    "Ortak oyun modu {0} Atölye'den kuruldu. Kullanmak için oyunu yeniden başlat.",
    "協力プレイMOD {0}をワークショップからインストールしました。使うにはゲームを再起動してください。",
    "已从创意工坊安装联机模组 {0}。请重启游戏以使用。",
    "창작마당에서 협동 모드 {0}을(를) 설치했습니다. 사용하려면 게임을 다시 시작하세요.")
row("Could not install the co-op mod update from the Workshop: {0}",
    "Impossible d'installer la mise à jour du mod coop depuis le Workshop : {0}",
    "No se pudo instalar la actualización del mod cooperativo desde el Workshop: {0}",
    "Não foi possível instalar a atualização do mod co-op pelo Workshop: {0}",
    "Nie udało się zainstalować aktualizacji moda co-op z Warsztatu: {0}",
    "Не удалось установить обновление мода кооператива из Мастерской: {0}",
    "Ortak oyun modu güncellemesi Atölye'den kurulamadı: {0}",
    "ワークショップから協力プレイMODの更新をインストールできませんでした：{0}",
    "无法从创意工坊安装联机模组更新：{0}",
    "창작마당에서 협동 모드 업데이트를 설치하지 못했습니다: {0}")

row("Hosting co-op in Graveyard Keeper 2",
    "Héberge une partie coop de Graveyard Keeper 2", "Organiza una partida cooperativa de Graveyard Keeper 2",
    "Hospedando co-op em Graveyard Keeper 2", "Prowadzi grę co-op w Graveyard Keeper 2", "Создал кооператив в Graveyard Keeper 2",
    "Graveyard Keeper 2'de ortak oyun kurdu", "Graveyard Keeper 2で協力プレイをホスト中", "正在 Graveyard Keeper 2 中开联机合作",
    "Graveyard Keeper 2 협동 호스트 중")
row("Waiting for players on port {0}.",
    "En attente de joueurs sur le port {0}.", "Esperando jugadores en el puerto {0}.", "Aguardando jogadores na porta {0}.",
    "Czekanie na graczy na porcie {0}.", "Ожидание игроков на порту {0}.", "{0} portunda oyuncular bekleniyor.",
    "ポート{0}でプレイヤーを待っています。", "正在端口 {0} 上等待玩家。", "포트 {0}에서 플레이어를 기다리는 중입니다.")
row("Connected to {0}.",
    "Connecté à {0}.", "Conectado a {0}.", "Conectado a {0}.", "Połączono z {0}.", "Подключено к {0}.", "{0} adresine bağlanıldı.",
    "{0}に接続しました。", "已连接到 {0}。", "{0}에 연결되었습니다.")

# ---------------------------------------------------------------- watching a scene together
row("A scene is starting",
    "Une scène commence", "Empieza una escena", "Uma cena está começando", "Zaczyna się scena", "Начинается сцена", "Bir sahne başlıyor",
    "シーンが始まります", "剧情即将开始", "장면이 시작됩니다")
row("{0} is watching a scene.",
    "{0} regarde une scène.", "{0} está viendo una escena.", "{0} está vendo uma cena.",
    "{0} ogląda scenę.", "{0} смотрит сцену.", "{0} bir sahne izliyor.",
    "{0}がシーンを見ています。", "{0} 正在观看剧情。", "{0} 님이 장면을 보고 있습니다.")
row("Watching together.",
    "Vous regardez ensemble.", "Lo veis juntos.", "Vocês assistem juntos.", "Oglądacie razem.", "Вы смотрите вместе.", "Birlikte izliyorsunuz.",
    "一緒に見ています。", "一起观看中。", "함께 보는 중입니다.")
row("You keep playing.",
    "Vous continuez à jouer.", "Sigues jugando.", "Você continua jogando.", "Grasz dalej.", "Вы продолжаете играть.", "Oynamaya devam ediyorsun.",
    "プレイを続けます。", "你继续游戏。", "계속 플레이합니다.")
row("Closes in {0} s.",
    "Se ferme dans {0} s.", "Se cierra en {0} s.", "Fecha em {0} s.", "Zamknie się za {0} s.", "Закроется через {0} с.", "{0} sn içinde kapanır.",
    "あと{0}秒で閉じます。", "{0} 秒后关闭。", "{0}초 후 닫힙니다.")
row("Watch",
    "Regarder", "Ver", "Assistir", "Oglądaj", "Смотреть", "İzle", "見る", "观看", "보기")
row("Keep playing",
    "Continuer à jouer", "Seguir jugando", "Continuar jogando", "Graj dalej", "Играть дальше", "Oynamaya devam et", "プレイを続ける", "继续游戏", "계속 플레이")
row("Watching a scene",
    "Vous regardez une scène", "Viendo una escena", "Assistindo a uma cena", "Oglądasz scenę", "Вы смотрите сцену", "Sahne izliyorsun",
    "シーンを観覧中", "正在观看剧情", "장면 관람 중")
row("Stop watching",
    "Arrêter de regarder", "Dejar de ver", "Parar de assistir", "Przestań oglądać", "Перестать смотреть", "İzlemeyi bırak",
    "観覧をやめる", "停止观看", "그만 보기")

# ---------------------------------------------------------------- chat and the status window
row("Say:",
    "Dire :", "Decir:", "Dizer:", "Powiedz:", "Сказать:", "Söyle:", "発言：", "说：", "말하기:")
row("Connecting",
    "Connexion", "Conectando", "Conectando", "Łączenie", "Подключение", "Bağlanılıyor", "接続中", "正在连接", "연결 중")
row("Copying the world",
    "Copie du monde", "Copiando el mundo", "Copiando o mundo", "Kopiowanie świata", "Копирование мира", "Dünya kopyalanıyor",
    "世界をコピー中", "正在复制世界", "세계 복사 중")
row("Loading the world",
    "Chargement du monde", "Cargando el mundo", "Carregando o mundo", "Wczytywanie świata", "Загрузка мира", "Dünya yükleniyor",
    "世界をロード中", "正在载入世界", "세계 불러오는 중")
row("Together",
    "Ensemble", "Juntos", "Juntos", "Razem", "Вместе", "Birlikte", "いっしょに", "已会合", "함께")
row("Something went wrong",
    "Un problème est survenu", "Algo salió mal", "Algo deu errado", "Coś poszło nie tak", "Что-то пошло не так", "Bir şeyler ters gitti",
    "問題が発生しました", "出了点问题", "문제가 발생했습니다")
row("OK",
    "OK", "Aceptar", "OK", "OK", "ОК", "Tamam", "OK", "确定", "확인")


# ---------------------------------------------------------------- the host's night setting (0.65.0)
row("The night passes when",
    "La nuit passe quand", "La noche pasa cuando", "A noite passa quando", "Noc mija, gdy", "Ночь проходит, когда", "Gece şu durumda geçer:",
    "夜が明ける条件", "夜晚过去的条件", "밤이 지나가는 조건")
row("Everyone sleeps",
    "Tout le monde dort", "Todos duermen", "Todos dormem", "Wszyscy śpią", "Спят все", "Herkes uyuyunca",
    "全員が眠る", "所有人都睡着", "모두 잠들 때")
row("I sleep",
    "Je dors", "Yo duermo", "Eu durmo", "Ja śpię", "Сплю я", "Ben uyuyunca",
    "自分が眠る", "我睡着", "내가 잘 때")
row("Your sleep passes the night for everyone. The others' clock jumps ahead with yours.",
    "Ton sommeil fait passer la nuit pour tous. L'horloge des autres avance avec la tienne.",
    "Tu sueño hace pasar la noche para todos. El reloj de los demás avanza con el tuyo.",
    "Seu sono faz a noite passar para todos. O relógio dos outros avança junto com o seu.",
    "Twój sen sprawia, że noc mija dla wszystkich. Zegar pozostałych przesuwa się razem z twoim.",
    "Твой сон проводит ночь для всех. Часы остальных переводятся вместе с твоими.",
    "Senin uykun geceyi herkes için geçirir. Diğerlerinin saati de seninkiyle ileri atlar.",
    "あなたが眠ると全員の夜が明けます。他のプレイヤーの時計もあなたに合わせて進みます。",
    "你睡觉时，所有人的夜晚都会过去。其他人的时钟会随你一起快进。",
    "당신이 자면 모두의 밤이 지나갑니다. 다른 사람들의 시계도 함께 앞당겨집니다.")
row("You sleep. The night passes for everyone.",
    "Tu dors. La nuit passe pour tout le monde.", "Duermes. La noche pasa para todos.", "Você dorme. A noite passa para todos.",
    "Śpisz. Noc mija dla wszystkich.", "Ты спишь. Ночь проходит для всех.", "Uyuyorsun. Gece herkes için geçiyor.",
    "あなたは眠りました。全員の夜が明けます。", "你睡着了。所有人的夜晚都过去了。", "잠이 들었습니다. 모두의 밤이 지나갑니다.")
row("{0} is asleep. The night passes.",
    "{0} dort. La nuit passe.", "{0} duerme. La noche pasa.", "{0} está dormindo. A noite passa.", "{0} śpi. Noc mija.",
    "{0} спит. Ночь проходит.", "{0} uyuyor. Gece geçiyor.", "{0}が眠りました。夜が明けます。", "{0}睡着了，夜晚过去了。", "{0} 님이 잠들었습니다. 밤이 지나갑니다.")

# ---------------------------------------------------------------- restored: added straight to the .cs files in 0.62-0.63.1
row('The session has ended', 'La session est terminée', 'La sesión ha terminado', 'A sessão terminou', 'Sesja się zakończyła', 'Сеанс завершён', 'Oturum sona erdi', 'セッションが終了しました', '联机已结束', '세션이 끝났습니다')
row("The connection to {0}'s game has ended. Nothing you do from here on is saved. OK takes you back to the main menu, where you can join again.", "La connexion à la partie de {0} est terminée. Rien de ce que tu fais à partir de maintenant n'est sauvegardé. OK te ramène au menu principal, où tu peux rejoindre à nouveau.", 'La conexión con la partida de {0} ha terminado. Nada de lo que hagas a partir de ahora se guarda. Aceptar te lleva al menú principal, donde puedes volver a unirte.', 'A conexão com o jogo de {0} terminou. Nada do que você fizer daqui em diante é salvo. OK leva você de volta ao menu principal, onde pode entrar de novo.', 'Połączenie z grą gracza {0} zostało zakończone. Nic, co zrobisz od teraz, nie zostanie zapisane. OK przeniesie cię do menu głównego, gdzie możesz dołączyć ponownie.', 'Соединение с игрой {0} прервано. Всё, что ты сделаешь дальше, не сохранится. ОК вернёт тебя в главное меню, где можно присоединиться снова.', '{0} adlı oyuncunun oyunuyla bağlantı sona erdi. Bundan sonra yaptıkların kaydedilmez. Tamam seni ana menüye götürür; oradan yeniden katılabilirsin.', '{0}のゲームとの接続が切れました。ここから先の行動は保存されません。OKでメインメニューに戻り、そこから再び参加できます。', '与{0}的游戏的连接已断开。之后的操作都不会被保存。按“确定”返回主菜单，可在那里重新加入。', '{0} 님의 게임과의 연결이 끊어졌습니다. 지금부터 하는 일은 저장되지 않습니다. 확인을 누르면 메인 메뉴로 돌아가며, 그곳에서 다시 참가할 수 있습니다.')
row('Could not reach {0} over Steam. Make sure they are hosting and already in their world, then try again.', "Impossible de joindre {0} via Steam. Vérifie qu'il héberge et qu'il est déjà dans son monde, puis réessaie.", 'No se pudo conectar con {0} por Steam. Comprueba que esté alojando y ya en su mundo, y vuelve a intentarlo.', 'Não foi possível alcançar {0} pela Steam. Veja se ele está hospedando e já no mundo dele, e tente de novo.', 'Nie udało się połączyć przez Steam z gospodarzem ({0}). Upewnij się, że hostuje i jest już w swoim świecie, i spróbuj ponownie.', 'Не удалось связаться через Steam с хостом ({0}). Убедись, что он хостит и уже в своём мире, и попробуй снова.', 'Steam üzerinden kurucuya ({0}) bağlanılamadı. Oyunu kurduğundan ve dünyasında olduğundan emin ol, sonra yeniden dene.', 'Steamで{0}に接続できませんでした。相手がホストしていて、すでに自分の世界にいることを確かめてから、もう一度試してください。', '无法通过 Steam 连接到{0}。请确认对方正在开房并且已经进入自己的世界，然后重试。', 'Steam으로 호스트({0})에게 연결하지 못했습니다. 상대가 호스트 중이고 이미 자기 세계에 있는지 확인한 뒤 다시 시도하세요.')
row('your friend', 'ton ami', 'tu amigo', 'seu amigo', 'twój znajomy', 'твой друг', 'arkadaşın', 'フレンド', '你的好友', '친구')
row('The host has co-op mod {0}, you have {1}. Both players need the same version.', "L'hôte a le mod coop {0}, tu as {1}. Les deux joueurs doivent avoir la même version.", 'El anfitrión tiene el mod cooperativo {0} y tú tienes {1}. Ambos jugadores necesitan la misma versión.', 'O anfitrião tem o mod co-op {0} e você tem {1}. Os dois jogadores precisam da mesma versão.', 'Gospodarz ma mod co-op {0}, a ty {1}. Obaj gracze potrzebują tej samej wersji.', 'У хоста кооп-мод {0}, у тебя {1}. Обоим игрокам нужна одна и та же версия.', 'Kurucuda co-op modu {0}, sende {1} var. İki oyuncunun da aynı sürüme ihtiyacı var.', 'ホストの協力プレイMODは{0}、あなたは{1}です。両方のプレイヤーが同じバージョンを使う必要があります。', '房主的联机模组是 {0}，你的是 {1}。双方需要使用相同的版本。', '호스트의 협동 모드는 {0}, 당신은 {1}입니다. 두 플레이어가 같은 버전을 사용해야 합니다.')
row('The host has a different co-op mod version; you have {0}. Both players need the same version.', "L'hôte a une autre version du mod coop ; tu as {0}. Les deux joueurs doivent avoir la même version.", 'El anfitrión tiene otra versión del mod cooperativo; tú tienes {0}. Ambos jugadores necesitan la misma versión.', 'O anfitrião tem outra versão do mod co-op; você tem {0}. Os dois jogadores precisam da mesma versão.', 'Gospodarz ma inną wersję moda co-op; ty masz {0}. Obaj gracze potrzebują tej samej wersji.', 'У хоста другая версия кооп-мода; у тебя {0}. Обоим игрокам нужна одна и та же версия.', 'Kurucuda co-op modunun farklı bir sürümü var; sende {0} var. İki oyuncunun da aynı sürüme ihtiyacı var.', 'ホストの協力プレイMODのバージョンが異なります（あなたは{0}）。両方のプレイヤーが同じバージョンを使う必要があります。', '房主的联机模组版本不同；你的是 {0}。双方需要使用相同的版本。', '호스트의 협동 모드 버전이 다릅니다(당신은 {0}). 두 플레이어가 같은 버전을 사용해야 합니다.')
row('The host has no loaded save slot. Save the game first, then retry.', "L'hôte n'a pas de sauvegarde chargée. Il doit d'abord sauvegarder, puis réessaie.", 'El anfitrión no tiene ninguna partida cargada. Que guarde primero y vuelve a intentarlo.', 'O anfitrião não tem um jogo salvo carregado. Ele precisa salvar primeiro; depois tente de novo.', 'Gospodarz nie ma wczytanego zapisu. Niech najpierw zapisze grę, potem spróbuj ponownie.', 'У хоста нет загруженного сохранения. Пусть сначала сохранится, потом попробуй снова.', 'Kurucunun yüklü bir kaydı yok. Önce kaydetsin, sonra tekrar dene.', 'ホストにロード済みのセーブデータがありません。先にセーブしてから、もう一度試してください。', '房主没有已加载的存档。请房主先存档，然后重试。', '호스트에 불러온 저장 데이터가 없습니다. 먼저 저장한 뒤 다시 시도하세요.')
row('The host is already sending a save to another player.', "L'hôte envoie déjà sa sauvegarde à un autre joueur.", 'El anfitrión ya está enviando su partida a otro jugador.', 'O anfitrião já está enviando o jogo salvo para outro jogador.', 'Gospodarz już wysyła swój zapis innemu graczowi.', 'Хост уже отправляет своё сохранение другому игроку.', 'Kurucu kaydını şu anda başka bir oyuncuya gönderiyor.', 'ホストは別のプレイヤーにセーブデータを送信中です。', '房主正在向另一位玩家发送存档。', '호스트가 이미 다른 플레이어에게 저장 데이터를 보내고 있습니다.')
row('You were brought back from the fight area.', 'Tu as été ramené hors de la zone de combat.', 'Te han sacado de la zona de combate.', 'Você foi trazido de volta da área de combate.', 'Zostałeś zabrany z obszaru walki.', 'Тебя вернули из зоны боя.', 'Savaş alanından geri getirildin.', '戦闘エリアから戻されました。', '你已被带离战斗区域。', '전투 지역에서 돌아왔습니다.')

row("Your friend's game is not open to join right now. If they are hosting, try again in a moment.",
    "La partie de ton ami n'est pas ouverte pour le moment. S'il héberge, réessaie dans un instant.",
    'La partida de tu amigo no está abierta ahora mismo. Si está alojando, vuelve a intentarlo en un momento.',
    'O jogo do seu amigo não está aberto para entrar agora. Se ele estiver hospedando, tente de novo em instantes.',
    'Gra twojego znajomego nie jest teraz otwarta. Jeśli hostuje, spróbuj ponownie za chwilę.',
    'Игра твоего друга сейчас закрыта для входа. Если он хостит, попробуй ещё раз чуть позже.',
    'Arkadaşının oyunu şu anda katılıma açık değil. Oyunu kuruyorsa birazdan yeniden dene.',
    'フレンドのゲームには今は参加できません。ホスト中なら、少し待ってからもう一度試してください。',
    '你好友的游戏目前无法加入。如果对方正在开房，请稍后再试。',
    '친구의 게임에 지금은 참가할 수 없습니다. 호스트 중이라면 잠시 후 다시 시도하세요.')
