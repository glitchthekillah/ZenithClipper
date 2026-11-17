const LOGO = {"nvidia": "M8.948 8.798v-1.43a6.7 6.7 0 0 1 .424-.018c3.922-.124 6.493 3.374 6.493 3.374s-2.774 3.851-5.75 3.851c-.398 0-.787-.062-1.158-.185v-4.346c1.528.185 1.837.857 2.747 2.385l2.04-1.714s-1.492-1.952-4-1.952a6.016 6.016 0 0 0-.796.035m0-4.735v2.138l.424-.027c5.45-.185 9.01 4.47 9.01 4.47s-4.08 4.964-8.33 4.964c-.37 0-.733-.035-1.095-.097v1.325c.3.035.61.062.91.062 3.957 0 6.82-2.023 9.593-4.408.459.371 2.34 1.263 2.73 1.652-2.633 2.208-8.772 3.984-12.253 3.984-.335 0-.653-.018-.971-.053v1.864H24V4.063zm0 10.326v1.131c-3.657-.654-4.673-4.46-4.673-4.46s1.758-1.944 4.673-2.262v1.237H8.94c-1.528-.186-2.73 1.245-2.73 1.245s.68 2.412 2.739 3.11M2.456 10.9s2.164-3.197 6.5-3.533V6.201C4.153 6.59 0 10.653 0 10.653s2.35 6.802 8.948 7.42v-1.237c-4.84-.6-6.492-5.936-6.492-5.936z", "amd": "M18.324 9.137l1.559 1.56h2.556v2.557L24 14.814V9.137zM2 9.52l-2 4.96h1.309l.37-.982H3.9l.408.982h1.338L3.432 9.52zm4.209 0v4.955h1.238v-3.092l1.338 1.562h.188l1.338-1.556v3.091h1.238V9.52H10.47l-1.592 1.845L7.287 9.52zm6.283 0v4.96h2.057c1.979 0 2.88-1.046 2.88-2.472 0-1.36-.937-2.488-2.747-2.488zm1.237.91h.792c1.17 0 1.63.711 1.63 1.57 0 .728-.372 1.572-1.616 1.572h-.806zm-10.985.273l.791 1.932H2.008zm17.137.307l-1.604 1.603v2.25h2.246l1.604-1.607h-2.246z", "intel": "M20.42 7.345v9.18h1.651v-9.18zM0 7.475v1.737h1.737V7.474zm9.78.352v6.053c0 .513.044.945.13 1.292.087.34.235.618.44.828.203.21.475.359.803.451.334.093.754.136 1.255.136h.216v-1.533c-.24 0-.445-.012-.593-.037a.672.672 0 0 1-.39-.173.693.693 0 0 1-.173-.377 4.002 4.002 0 0 1-.037-.606v-2.182h1.193v-1.416h-1.193V7.827zm-3.505 2.312c-.396 0-.76.08-1.082.241-.327.161-.6.384-.822.668l-.087.117v-.902H2.658v6.256h1.639v-3.214c.018-.588.16-1.02.433-1.299.29-.297.642-.445 1.044-.445.476 0 .841.149 1.082.433.235.284.359.686.359 1.2v3.324h1.663V12.97c.006-.89-.229-1.595-.686-2.09-.458-.495-1.1-.742-1.917-.742zm10.065.006a3.252 3.252 0 0 0-2.306.946c-.29.29-.525.637-.692 1.033a3.145 3.145 0 0 0-.254 1.273c0 .452.08.878.241 1.274.161.395.39.742.674 1.032.284.29.637.526 1.045.693.408.173.86.26 1.342.26 1.397 0 2.262-.637 2.782-1.23l-1.187-.904c-.248.297-.841.699-1.583.699-.464 0-.847-.105-1.138-.321a1.588 1.588 0 0 1-.593-.872l-.019-.056h4.915v-.587c0-.451-.08-.872-.235-1.267a3.393 3.393 0 0 0-.661-1.033 3.013 3.013 0 0 0-1.02-.692 3.345 3.345 0 0 0-1.311-.248zm-16.297.118v6.256h1.651v-6.256zm16.278 1.286c1.132 0 1.664.797 1.664 1.255l-3.32.006c0-.458.525-1.255 1.656-1.261zm7.073 3.814a.606.606 0 0 0-.606.606.606.606 0 0 0 .606.606.606.606 0 0 0 .606-.606.606.606 0 0 0-.606-.606zm-.008.105a.5.5 0 0 1 .002 0 .5.5 0 0 1 .5.501.5.5 0 0 1-.5.5.5.5 0 0 1-.5-.5.5.5 0 0 1 .498-.5zm-.233.155v.699h.13v-.285h.093l.173.285h.136l-.18-.297a.191.191 0 0 0 .118-.056c.03-.03.05-.074.05-.136 0-.068-.02-.117-.063-.154-.037-.038-.105-.056-.185-.056zm.13.099h.154c.019 0 .037.006.056.012a.064.064 0 0 1 .037.031c.013.013.012.031.012.056a.124.124 0 0 1-.012.055.164.164 0 0 1-.037.031c-.019.006-.037.013-.056.013h-.154Z", "linux": "M12.504 0c-.155 0-.315.008-.48.021-4.226.333-3.105 4.807-3.17 6.298-.076 1.092-.3 1.953-1.05 3.02-.885 1.051-2.127 2.75-2.716 4.521-.278.832-.41 1.684-.287 2.489a.424.424 0 00-.11.135c-.26.268-.45.6-.663.839-.199.199-.485.267-.797.4-.313.136-.658.269-.864.68-.09.189-.136.394-.132.602 0 .199.027.4.055.536.058.399.116.728.04.97-.249.68-.28 1.145-.106 1.484.174.334.535.47.94.601.81.2 1.91.135 2.774.6.926.466 1.866.67 2.616.47.526-.116.97-.464 1.208-.946.587-.003 1.23-.269 2.26-.334.699-.058 1.574.267 2.577.2.025.134.063.198.114.333l.003.003c.391.778 1.113 1.132 1.884 1.071.771-.06 1.592-.536 2.257-1.306.631-.765 1.683-1.084 2.378-1.503.348-.199.629-.469.649-.853.023-.4-.2-.811-.714-1.376v-.097l-.003-.003c-.17-.2-.25-.535-.338-.926-.085-.401-.182-.786-.492-1.046h-.003c-.059-.054-.123-.067-.188-.135a.357.357 0 00-.19-.064c.431-1.278.264-2.55-.173-3.694-.533-1.41-1.465-2.638-2.175-3.483-.796-1.005-1.576-1.957-1.56-3.368.026-2.152.236-6.133-3.544-6.139zm.529 3.405h.013c.213 0 .396.062.584.198.19.135.33.332.438.533.105.259.158.459.166.724 0-.02.006-.04.006-.06v.105a.086.086 0 01-.004-.021l-.004-.024a1.807 1.807 0 01-.15.706.953.953 0 01-.213.335.71.71 0 00-.088-.042c-.104-.045-.198-.064-.284-.133a1.312 1.312 0 00-.22-.066c.05-.06.146-.133.183-.198.053-.128.082-.264.088-.402v-.02a1.21 1.21 0 00-.061-.4c-.045-.134-.101-.2-.183-.333-.084-.066-.167-.132-.267-.132h-.016c-.093 0-.176.03-.262.132a.8.8 0 00-.205.334 1.18 1.18 0 00-.09.4v.019c.002.089.008.179.02.267-.193-.067-.438-.135-.607-.202a1.635 1.635 0 01-.018-.2v-.02a1.772 1.772 0 01.15-.768c.082-.22.232-.406.43-.533a.985.985 0 01.594-.2zm-2.962.059h.036c.142 0 .27.048.399.135.146.129.264.288.344.465.09.199.14.4.153.667v.004c.007.134.006.2-.002.266v.08c-.03.007-.056.018-.083.024-.152.055-.274.135-.393.2.012-.09.013-.18.003-.267v-.015c-.012-.133-.04-.2-.082-.333a.613.613 0 00-.166-.267.248.248 0 00-.183-.064h-.021c-.071.006-.13.04-.186.132a.552.552 0 00-.12.27.944.944 0 00-.023.33v.015c.012.135.037.2.08.334.046.134.098.2.166.268.01.009.02.018.034.024-.07.057-.117.07-.176.136a.304.304 0 01-.131.068 2.62 2.62 0 01-.275-.402 1.772 1.772 0 01-.155-.667 1.759 1.759 0 01.08-.668 1.43 1.43 0 01.283-.535c.128-.133.26-.2.418-.2zm1.37 1.706c.332 0 .733.065 1.216.399.293.2.523.269 1.052.468h.003c.255.136.405.266.478.399v-.131a.571.571 0 01.016.47c-.123.31-.516.643-1.063.842v.002c-.268.135-.501.333-.775.465-.276.135-.588.292-1.012.267a1.139 1.139 0 01-.448-.067 3.566 3.566 0 01-.322-.198c-.195-.135-.363-.332-.612-.465v-.005h-.005c-.4-.246-.616-.512-.686-.71-.07-.268-.005-.47.193-.6.224-.135.38-.271.483-.336.104-.074.143-.102.176-.131h.002v-.003c.169-.202.436-.47.839-.601.139-.036.294-.065.466-.065zm2.8 2.142c.358 1.417 1.196 3.475 1.735 4.473.286.534.855 1.659 1.102 3.024.156-.005.33.018.513.064.646-1.671-.546-3.467-1.089-3.966-.22-.2-.232-.335-.123-.335.59.534 1.365 1.572 1.646 2.757.13.535.16 1.104.021 1.67.067.028.135.06.205.067 1.032.534 1.413.938 1.23 1.537v-.043c-.06-.003-.12 0-.18 0h-.016c.151-.467-.182-.825-1.065-1.224-.915-.4-1.646-.336-1.77.465-.008.043-.013.066-.018.135-.068.023-.139.053-.209.064-.43.268-.662.669-.793 1.187-.13.533-.17 1.156-.205 1.869v.003c-.02.334-.17.838-.319 1.35-1.5 1.072-3.58 1.538-5.348.334a2.645 2.645 0 00-.402-.533 1.45 1.45 0 00-.275-.333c.182 0 .338-.03.465-.067a.615.615 0 00.314-.334c.108-.267 0-.697-.345-1.163-.345-.467-.931-.995-1.788-1.521-.63-.4-.986-.87-1.15-1.396-.165-.534-.143-1.085-.015-1.645.245-1.07.873-2.11 1.274-2.763.107-.065.037.135-.408.974-.396.751-1.14 2.497-.122 3.854a8.123 8.123 0 01.647-2.876c.564-1.278 1.743-3.504 1.836-5.268.048.036.217.135.289.202.218.133.38.333.59.465.21.201.477.335.876.335.039.003.075.006.11.006.412 0 .73-.134.997-.268.29-.134.52-.334.74-.4h.005c.467-.135.835-.402 1.044-.7zm2.185 8.958c.037.6.343 1.245.882 1.377.588.134 1.434-.333 1.791-.765l.211-.01c.315-.007.577.01.847.268l.003.003c.208.199.305.53.391.876.085.4.154.78.409 1.066.486.527.645.906.636 1.14l.003-.007v.018l-.003-.012c-.015.262-.185.396-.498.595-.63.401-1.746.712-2.457 1.57-.618.737-1.37 1.14-2.036 1.191-.664.053-1.237-.2-1.574-.898l-.005-.003c-.21-.4-.12-1.025.056-1.69.176-.668.428-1.344.463-1.897.037-.714.076-1.335.195-1.814.12-.465.308-.797.641-.984l.045-.022zm-10.814.049h.01c.053 0 .105.005.157.014.376.055.706.333 1.023.752l.91 1.664.003.003c.243.533.754 1.064 1.189 1.637.434.598.77 1.131.729 1.57v.006c-.057.744-.48 1.148-1.125 1.294-.645.135-1.52.002-2.395-.464-.968-.536-2.118-.469-2.857-.602-.369-.066-.61-.2-.723-.4-.11-.2-.113-.602.123-1.23v-.004l.002-.003c.117-.334.03-.752-.027-1.118-.055-.401-.083-.71.043-.94.16-.334.396-.4.69-.533.294-.135.64-.202.915-.47h.002v-.002c.256-.268.445-.601.668-.838.19-.201.38-.336.663-.336zm7.159-9.074c-.435.201-.945.535-1.488.535-.542 0-.97-.267-1.28-.466-.154-.134-.28-.268-.373-.335-.164-.134-.144-.333-.074-.333.109.016.129.134.199.2.096.066.215.2.36.333.292.2.68.467 1.167.467.485 0 1.053-.267 1.398-.466.195-.135.445-.334.648-.467.156-.136.149-.267.279-.267.128.016.034.134-.147.332a8.097 8.097 0 01-.69.468zm-1.082-1.583V5.64c-.006-.02.013-.042.029-.05.074-.043.18-.027.26.004.063 0 .16.067.15.135-.006.049-.085.066-.135.066-.055 0-.092-.043-.141-.068-.052-.018-.146-.008-.163-.065zm-.551 0c-.02.058-.113.049-.166.066-.047.025-.086.068-.14.068-.05 0-.13-.02-.136-.068-.01-.066.088-.133.15-.133.08-.031.184-.047.259-.005.019.009.036.03.03.05v.02h.003z", "obsstudio": "M12,24C5.383,24,0,18.617,0,12S5.383,0,12,0s12,5.383,12,12S18.617,24,12,24z M12,1.109 C5.995,1.109,1.11,5.995,1.11,12C1.11,18.005,5.995,22.89,12,22.89S22.89,18.005,22.89,12C22.89,5.995,18.005,1.109,12,1.109z M6.182,5.99c0.352-1.698,1.503-3.229,3.05-3.996c-0.269,0.273-0.595,0.483-0.844,0.78c-1.02,1.1-1.48,2.692-1.199,4.156 c0.355,2.235,2.455,4.06,4.732,4.028c1.765,0.079,3.485-0.937,4.348-2.468c1.848,0.063,3.645,1.017,4.7,2.548 c0.54,0.799,0.962,1.736,0.991,2.711c-0.342-1.295-1.202-2.446-2.375-3.095c-1.135-0.639-2.529-0.802-3.772-0.425 c-1.56,0.448-2.849,1.723-3.293,3.293c-0.377,1.25-0.216,2.628,0.377,3.772c-0.825,1.429-2.315,2.449-3.932,2.756 c-1.244,0.261-2.551,0.059-3.709-0.464c1.036,0.302,2.161,0.355,3.191-0.011c1.381-0.457,2.522-1.567,3.024-2.935 c0.556-1.49,0.345-3.261-0.591-4.54c-0.7-1.007-1.803-1.717-3.002-1.969c-0.38-0.068-0.764-0.098-1.148-0.134 c-0.611-1.231-0.834-2.66-0.528-3.996L6.182,5.99z", "steelseries": "M14.08008 0c-1.00234 0-1.8125.80893-1.8125 1.8086v2.57226c-4.01871.7444-7.19505 3.9119-7.93946 7.91992H1.8125c-1.001 0-1.8125.80698-1.8125 1.80664 0 .99833.8115 1.8086 1.8125 1.8086h2.51563C5.18077 20.5094 9.22875 24 14.08008 24 19.54884 24 24 19.56148 24 14.10742c0-4.83662-3.50067-8.87524-8.10742-9.72656V1.80859C15.89258.80893 15.08108 0 14.08008 0ZM4.69336 3.17578c-1.00368 0-1.8164.80955-1.8164 1.81055 0 .99966.81272 1.8125 1.8164 1.8125 1.001 0 1.8164-.81284 1.8164-1.8125 0-1.001-.8154-1.81055-1.8164-1.81055zm9.38672 4.65625c3.46809 0 6.29297 2.81398 6.29297 6.2754 0 3.46006-2.82488 6.27734-6.29297 6.27734-3.46943 0-6.29297-2.81728-6.29297-6.27735 0-3.4614 2.82354-6.27539 6.29297-6.27539zm-.01758 2.4043c-2.14634 0-3.89258 1.73986-3.89258 3.88086S11.91616 18 14.0625 18c2.14634 0 3.89258-1.74182 3.89258-3.88281 0-2.141-1.74624-3.88086-3.89258-3.88086zm0 2.7168c.6455 0 1.16797.51989 1.16797 1.16406 0 .64283-.52246 1.16797-1.16797 1.16797-.64417 0-1.16992-.52514-1.16992-1.16797 0-.64417.52575-1.16407 1.16992-1.16407z", "windows": "M0 3.45 9.75 2.1v9.45H0zm10.95-1.5L24 0v11.4H10.95zM0 12.6h9.75v9.45L0 20.7zm10.95 0H24V24l-13.05-1.8z"};
const lg = (k) => k === 'medal' ? '<span class="lg-m">M</span>' : k === 'zc' ? '<span class="orb-css lg"></span>' : `<svg class="lg" viewBox="0 0 24 24" aria-hidden="true"><path d="${LOGO[k]}"/></svg>`;
const brandOf = (t) => /^NVIDIA/.test(t) ? 'nvidia' : /^AMD/.test(t) ? 'amd' : /^Intel/.test(t) ? 'intel' : /^Windows/.test(t) ? 'windows' : /^Linux/.test(t) ? 'linux' : /^Medal/.test(t) ? 'medal' : /^OBS/.test(t) ? 'obsstudio' : /^SteelSeries/.test(t) ? 'steelseries' : /^ZenithClipper/.test(t) ? 'zc' : null;
(() => {
const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
const $ = (s, r = document) => r.querySelector(s), $$ = (s, r = document) => [...r.querySelectorAll(s)];
const clamp = (x, a = 0, b = 1) => Math.max(a, Math.min(b, x));
const seg = (p, a, b) => clamp((p - a) / (b - a));
const ease = (x) => 1 - Math.pow(1 - x, 3);
const lerp = (a, b, t) => a + (b - a) * t;

/* ---------- Icons (stroke, like the app's Fluent glyphs) ---------- */
const P = {
  gear: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>', check: '<path d="M5 12.5l4.5 4.5L19 7"/>', plus: '<path d="M12 5v14M5 12h14"/>',
  down: '<path d="M6 9l6 6 6-6"/>', right: '<path d="M9 6l6 6-6 6"/>', list: '<path d="M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01"/>',
  undo: '<path d="M9 14 4 9l5-5"/><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11"/>', redo: '<path d="m15 14 5-5-5-5"/><path d="M20 9H9.5a5.5 5.5 0 0 0 0 11H13"/>',
  upload: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12"/>', min: '<path d="M5 12h14"/>', sq: '<rect x="5" y="5" width="14" height="14" rx="1"/>',
  scissors: '<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M20 4 8.1 15.9M14.5 14.5 20 20M8.1 8.1 12 12"/>',
  pointer: '<path d="m4 4 7 17 2.5-7.5L21 11z"/>', ffwd: '<path d="m6 7 5 5-5 5M13 7l5 5-5 5"/>', ripple: '<path d="M6 4v16M10 12h10M16 8l4 4-4 4"/>',
  slip: '<path d="M12 4v16M8 8l-4 4 4 4M16 8l4 4-4 4"/>', hand: '<path d="M18 11V6a2 2 0 0 0-4 0v5M14 10V4a2 2 0 0 0-4 0v6M10 10.5V6a2 2 0 0 0-4 0v8"/><path d="M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.9-6-2.4l-3.6-3.6a2 2 0 0 1 2.8-2.8L7 15"/>',
  zoom: '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3M11 8v6M8 11h6"/>', trash: '<path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6M10 11v6M14 11v6"/>',
  back: '<path d="M18 6H8M8 6v12M4 12h10"/>', mark: '<path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"/>',
  film: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m10 9 5 3-5 3z"/>', vol: '<path d="M11 5 6 9H2v6h4l5 4zM15.5 8.5a5 5 0 0 1 0 7M19 5a10 10 0 0 1 0 14"/>',
  type: '<path d="M4 7V4h16v3M9 20h6M12 4v16"/>', wand: '<path d="m15 4 5 5L9 20H4v-5zM13 6l5 5"/>', cc: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M10 10.5a2 2 0 1 0 0 3M17 10.5a2 2 0 1 0 0 3"/>',
  full: '<path d="M8 3H5a2 2 0 0 0-2 2v3M21 8V5a2 2 0 0 0-2-2h-3M3 16v3a2 2 0 0 0 2 2h3M16 21h3a2 2 0 0 0 2-2v-3"/>', eye: '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
  lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>', head: '<path d="M3 18v-6a9 9 0 0 1 18 0v6"/><path d="M21 19a2 2 0 0 1-2 2h-1v-6h3zM3 19a2 2 0 0 0 2 2h1v-6H3z"/>',
  skipb: '<path d="M19 20 9 12l10-8zM5 19V5"/>', stepb: '<path d="M17 18 9 12l8-6z"/>', stepf: '<path d="m7 6 8 6-8 6z"/>', skipf: '<path d="m5 4 10 8-10 8zM19 5v14"/>',
  play: '<path d="M7 4v16l13-8z"/>', move: '<path d="M5 9l-3 3 3 3M9 5l3-3 3 3M15 19l-3 3-3-3M19 9l3 3-3 3M2 12h20M12 2v20"/>', mute: '<path d="M11 5 6 9H2v6h4l5 4zM22 9l-6 6M16 9l6 6"/>',
  split: '<path d="M12 3v18M8 7l-4 5 4 5M16 7l4 5-4 5"/>', clip: '<rect x="3" y="7" width="18" height="10" rx="2"/>'
};
const I = (n) => `<svg viewBox="0 0 24 24" aria-hidden="true">${P[n]}</svg>`;

/* ---------- The panel, rebuilt from PanelForm / Dashboard.cs ---------- */
function panelHTML(id) {
  return `<div class="zc zp" id="${id}">
  <div class="zp-h"><span class="orb"></span><b>ZenithClipper</b><span class="st"><i></i>Recording</span><span class="sep"></span>${I('gear')}<span style="width:4px"></span>${I('x')}</div>
  <div class="zp-b">
    <div class="zp-toast" data-toast>${I('check')}<span>Saved 2m clip  ·  Clip-2026-11-17-21-42-08-3f9a1c.mp4</span><em>Edit</em></div>
    <div class="zp-row"><b>Instant replay</b><span class="zp-tog">Record<i></i></span></div>
    <div class="zp-big"><div><div class="zp-rec"><i></i>RECORDING</div><div class="zp-time" data-clock>21:11</div><div class="zp-sub">buffered of 1:00:00 history</div></div><div class="zp-ring" data-ring><div><b data-mb>611 MB</b><span>of 2.0 GB</span></div></div></div>
    <div class="zp-chips"><span>NVENC NATIVE</span><span>2560×1600</span><span>Display fps</span><span>Audio on</span></div>
    <div class="zp-strip" data-strip><div class="segs">${'<i></i>'.repeat(6)}</div><div class="pick"></div><div class="now"></div></div>
    <div class="zp-strip-l"><span>−1:00:00</span><span>Now</span></div>
    <div class="zp-durs" data-durs><div class="pill"></div><span class="on">15s</span><span>30s</span><span>1m</span><span>2m</span><span>5m</span><span>Custom</span></div>
    <div class="zp-save" data-save><span data-savet>Save last 15s</span><kbd>Alt+C</kbd></div>
    <div class="zp-tiles"><div><small>SAVED TODAY</small><b data-saved>0</b><span data-saveds>No clips yet</span></div><div><small>RECORDER CPU</small><b data-cpu>1.4%</b><span>of all cores</span></div><div><small>MEMORY</small><b>89 MB</b><span>recorder only</span></div></div>
  </div>
  <div class="zp-audio">
    <div class="zp-row"><b>Clip audio</b><span>Only changes what clips hear</span></div>
    <div class="zp-app" data-app="0"><span class="ic" style="background:#5865F2">D</span><span>Discord</span><span class="sl" style="--v:50%"><i></i><u></u></span><span class="v">100%</span>${I('vol')}</div>
    <div class="zp-app" data-app="1"><span class="ic" style="background:#1DB954">S</span><span>Spotify</span><span class="sl" style="--v:50%"><i></i><u></u></span><span class="v">100%</span>${I('vol')}</div>
    <div class="zp-app" data-app="2"><span class="ic" style="background:#1B2838">S</span><span>steam</span><span class="sl" style="--v:50%"><i></i><u></u></span><span class="v">100%</span>${I('vol')}</div>
  </div></div>`;
}
$('#hero-panel').innerHTML = panelHTML('hp');
$('#rp-fit').innerHTML = panelHTML('rp') + `<div class="rp-keys" id="rp-keys"><span class="key" id="k-alt">Alt</span>+<span class="key" id="k-c">C</span></div><div class="rp-note hand" id="rp-note1" style="left:-236px;top:166px">an hour of history →</div><div class="rp-note hand" id="rp-note2" style="left:-244px;top:222px">two minutes, please →</div>`;

/* ---------- The editor, rebuilt from EditorChrome.cs ---------- */
const PX = 128, LANE0 = 0; // pixels per second on the timeline
const bin = [['hero', 'hero.mp4', '0:06'], ['fighter', 'fighter.mp4', '0:05'], ['golf', 'golf.mp4', '0:05'], ['mech', 'mech.mp4', '0:05']];
$('#ed-fit').innerHTML = `<div class="zc ze" id="ze">
  <div class="ze-top"><span class="brand"><span class="orb"></span>ZenithClipper</span><span class="menu"><span>File</span><span>Edit</span><span>Sequence</span><span>Graphics</span><span>Captions</span><span>Window</span></span>
    <span class="doc"><b style="font-weight:500">Untitled</b><span id="ze-dirty">Saved</span></span>
    <span class="right">${I('undo')}${I('redo')}<span class="vsep"></span><span class="ze-export" id="ze-export">${I('upload')}Export</span><span class="ze-win"><span>${I('min')}</span><span>${I('sq')}</span><span>${I('x')}</span></span></span></div>
  <div class="ze-main">
    <div class="ze-left">
      <div class="ze-tabs"><span class="on">Media</span><span>Titles</span><span>Captions</span><span>Effects</span><span class="r" style="color:var(--soft)">${I('plus')}</span></div>
      <div class="ze-search"><div>Search clips</div><div class="sort">Added ${I('down')}</div><div style="width:30px;justify-content:center;color:var(--soft)">${I('list')}</div></div>
      <div class="ze-bin">${bin.map(([k, n, d], i) => `<div class="ze-card${i === 0 ? ' sel' : ''}" data-card="${i}"><div class="th"><video src="media/${k}.mp4" poster="media/${k}.jpg" muted loop playsinline preload="none"></video><span class="d">${d}</span></div><b>${n}</b><small>Video · 1280×720</small></div>`).join('')}</div>
      <div class="ze-add">${I('plus')}Add to timeline</div>
      <div class="ze-foot">4 clips · originals stay untouched</div>
    </div>
    <div class="ze-center">
      <div class="ze-tabs"><span style="color:var(--faint)">Source</span><span class="on">Program</span><span>Sequence 1</span><span class="r">1280×720 · 30 fps</span></div>
      <div class="ze-mon"><div class="frame"><video id="mon-hero" src="media/hero.mp4" muted loop playsinline preload="none" style="opacity:0"></video><video id="mon-fighter" src="media/fighter.mp4" muted loop playsinline preload="none" style="opacity:0"></video>
        <div class="empty" id="mon-empty"><i>${I('scissors')}</i><b>Make your first cut</b><span>Add a clip to the timeline<br>to preview your edit here.</span><em>Import media</em></div>
        <div class="ze-gg" id="ze-gg">GG</div></div></div>
      <div class="ze-trans"><span class="ze-tc" id="ze-tc">00:00:00.000</span><span id="ze-total">/ 00:00:00.000</span><span class="ze-btns">${I('skipb')}${I('stepb')}<span class="ze-play">${I('play')}</span>${I('stepf')}${I('skipf')}</span><span style="color:var(--soft);display:flex;gap:14px;align-items:center"><span style="padding:4px 10px;border:1px solid var(--line);border-radius:6px;display:flex;gap:18px">Fit ${I('down')}</span>${I('full')}</span></div>
    </div>
    <div class="ze-right">
      <div class="ze-tabs"><span class="on">Inspector</span><span>Mixer</span><span>Scopes</span><span>History</span></div>
      <div class="ze-insp" id="ze-insp"></div>
    </div>
  </div>
  <div class="ze-seqbar"><span>Sequence 1</span><span style="background:none;color:var(--soft);padding:7px 4px">${I('plus')}</span></div>
  <div class="ze-tools">
    <span class="t on" id="tool-sel">${I('pointer')}</span><span class="t">${I('ffwd')}</span><span class="t">${I('ripple')}</span><span class="t">${I('slip')}</span><span class="t" id="tool-cut">${I('scissors')}</span><span class="t">${I('split')}</span><span class="t">${I('clip')}</span><span class="t">${I('move')}</span><span class="t">${I('hand')}</span><span class="t">${I('zoom')}</span>
    <span class="vs"></span><span class="t">${I('scissors')}</span><span class="t">${I('trash')}</span><span class="t">${I('back')}</span><span class="t">${I('mark')}</span><span class="vs"></span>
    <span class="lbl snap"><i>${I('check')}</i>Snap</span><span class="vs"></span><span class="lbl">${I('film')}Video track</span><span class="lbl">${I('vol')}Audio track</span><span class="lbl" id="tool-title">${I('type')}Title</span><span class="t">${I('wand')}</span><span class="vs"></span><span class="lbl">${I('cc')}Auto captions</span>
    <span class="ze-zoom">${I('min')}<span class="bar"><u></u></span>${I('plus')}<span style="margin-left:8px">${I('full')}</span></span>
  </div>
  <div class="ze-tl" id="ze-tl">
    <div class="corner">TRACKS</div><div class="ze-ruler">${Array.from({ length: 12 }, (_, i) => `<span style="left:${i * PX}px">0:${String(i).padStart(2, '0')}</span>`).join('')}</div>
    <div class="hd" style="--c:#F5A524"><em>T</em><b>Titles</b>${I('eye')}${I('lock')}</div><div class="ze-lane" id="ln-t"><div class="ze-cl title" id="c-title" style="display:none"><div class="nm">GG</div><span class="edge"></span></div></div>
    <div class="hd" style="--c:#FF6B4A"><em>V1</em><b>Video 1</b>${I('eye')}${I('vol')}${I('lock')}</div><div class="ze-lane" id="ln-v">
      <div class="ze-cl" id="c-a" style="display:none"><div class="nm">hero.mp4</div><div class="fs" style="background-image:url(media/strip-hero.jpg)"></div><span class="edge"></span></div>
      <div class="ze-cl" id="c-b" style="display:none"><div class="nm">hero.mp4</div><div class="fs" style="background-image:url(media/strip-hero.jpg);background-position:-${2.7 * PX}px 0"></div><span class="edge"></span></div>
      <div class="ze-cl" id="c-f" style="display:none"><div class="nm">fighter.mp4</div><div class="fs" style="background-image:url(media/strip-fighter.jpg)"></div><span class="edge"></span></div>
      <div class="ze-cut" id="ze-cut"></div></div>
    <div class="hd" style="--c:#9A9EA8"><em>A1</em><b>Audio 1</b>${I('head')}${I('vol')}${I('lock')}</div><div class="ze-lane"></div>
    <div class="corner" style="border-bottom:0"></div><div></div>
    <div class="ze-ph" id="ze-ph" style="left:180px"></div>
  </div>
  <div class="ze-status"><i></i><span id="ze-msg">Imported 4 file(s). Add to timeline to create linked video and audio tracks.</span><span class="r">Space play · J K L shuttle · Ctrl+K cut · Ctrl+D dissolve</span></div>
  <div class="ze-ghost" id="ze-ghost">${I('play')}hero.mp4</div>
  <div class="ed-note hand" id="n-drag" style="left:300px;top:150px"><svg width="70" height="30" viewBox="0 0 70 30"><path d="M68 4C48 2 22 6 6 24M6 24l2-10M6 24l10-2"/></svg>drag it in</div>
  <div class="ed-note hand" id="n-cut" style="left:560px;top:612px">snip<svg width="54" height="26" viewBox="0 0 54 26"><path d="M2 4c18 0 32 6 46 18M48 22l-10-1M48 22l-1-10"/></svg></div>
  <div class="ed-note hand" id="n-trim" style="left:880px;top:600px"><svg width="54" height="26" viewBox="0 0 54 26"><path d="M52 4C34 4 20 10 6 22M6 22l10-1M6 22l1-10"/></svg>drag the edge</div>
  <div class="ed-note hand" id="n-dbl" style="left:300px;top:300px"><svg width="60" height="24" viewBox="0 0 60 24"><path d="M58 20C40 20 20 14 4 4M4 4l10 1M4 4l2 10"/></svg>double-click</div>
  <div class="ed-note hand" id="n-gg" style="left:1040px;top:120px"><svg width="60" height="24" viewBox="0 0 60 24"><path d="M58 4C40 6 22 12 6 20M6 20l9-5M6 20l10 2"/></svg>just type it</div>
</div>`;

/* ---------- Export dialog, rebuilt from the real dialog ---------- */
$('#ex-dlg').innerHTML = `<div class="zc zx"><h4>Export</h4>
  <div class="r">Preset<div><span id="ex-preset">Match sequence · H.264 high quality</span>${I('down')}</div></div>
  <div class="r">Range<div>Entire sequence${I('down')}</div></div><div class="r">Captions<div>Burn into video${I('down')}</div></div>
  <div class="r">Audio bitrate<div>Preset default${I('down')}</div></div><div class="r">Sample rate<div>48 kHz${I('down')}</div></div>
  <p id="ex-desc">Same size as the sequence. Visually lossless for most footage.<br>1280×720 · 00:00:10.000 long</p>
  <div class="b"><span>Cancel</span><span>Add to queue</span><span>Export…</span></div></div>`;
const presets = [['Match sequence · H.264 high quality', 'Same size as the sequence. Visually lossless for most footage.'], ['Discord · 10 MB', 'Fits Discord\'s free upload limit.'], ['YouTube 4K · H.265', 'Upscaled for YouTube\'s best quality tier.'], ['Animated GIF · 480p', 'For the group chat.'], ['Smallest file · AV1', 'Tiny files, great quality.'], ['Audio only · FLAC', 'Just the sound, lossless.']];
let pi = 0; setInterval(() => { if (reduce) return; pi = (pi + 1) % presets.length; $('#ex-preset').textContent = presets[pi][0]; $('#ex-desc').innerHTML = presets[pi][1] + '<br>1280×720 · 00:00:10.000 long'; }, 1800);
const fmts = ['MP4', 'H.265', 'AV1', 'MOV', 'MKV', 'WebM', 'GIF', 'ProRes', 'MP3', 'WAV', 'M4A', 'FLAC', 'NVENC', 'Discord', 'YouTube 4K'];
$('#mq-a').innerHTML = [...fmts, ...fmts].map((f) => `<span>${f}</span>`).join('');
$('#mq-b').innerHTML = [...fmts.slice().reverse(), ...fmts.slice().reverse()].map((f) => `<span>${f}</span>`).join('');

/* ---------- Games, comparison ---------- */
const games = [['hero', 'Overwatch 2'], ['fighter', 'Fighting game'], ['souls', 'Action RPG'], ['golf', 'Golf'], ['mech', 'Mech'], ['bf1', 'Battlefield 1'], ['race', 'Racing'], ['sim', 'Transport sim'], ['pool', 'Pool']];
$('#gg').innerHTML = games.map(([k, n]) => `<div class="g"><video src="media/${k === 'hero' ? 'hero' : 'g-' + k}.mp4" poster="media/g-${k}.jpg" muted loop playsinline preload="none"></video><span>${n}</span></div>`).join('');
const rows = ['Windows and Linux', 'NVIDIA, AMD and Intel GPUs', 'Built-in clip editor', 'No account needed', 'Under 100 MB of RAM'];
const vs = [['ZenithClipper', 'yyyyy', true], ['Medal', 'nyynn'], ['NVIDIA App', 'nnnyn'], ['OBS', 'yynyn'], ['SteelSeries Moments', 'nyyyq']];
$('#vs').innerHTML = vs.map(([n, v, us]) => `<div class="c${us ? ' us' : ''}"><h3>${us ? '<span class="orb-css"></span>' : lg(brandOf(n))}${n}</h3><ul>${rows.map((r, i) => `<li class="${v[i]}"><i>${v[i] === 'y' ? '✓' : v[i] === 'n' ? '–' : '?'}</i>${r}</li>`).join('')}</ul></div>`).join('');

$$('.hw b, .bar > b').forEach((b) => { const k = brandOf(b.textContent); if (k) b.insertAdjacentHTML('afterbegin', lg(k)); });

/* ---------- Hero reel and background ---------- */
const reelItems = [['clutch.', 'hero'], ['combo.', 'fighter'], ['hole-in-one.', 'golf'], ['dogfight.', 'mech'], ['boss kill.', 'souls'], ['photo finish.', 'race'], ['trick shot.', 'pool']];
$('#reel').innerHTML = reelItems.map(([w]) => `<li>${w}</li>`).join('');
$('#hero-bg').innerHTML = reelItems.map(([, k], i) => `<video src="media/${k}.mp4" poster="media/${k === 'hero' ? 'hero' : k}.jpg" muted loop playsinline preload="${i === 0 ? 'auto' : 'none'}"${i === 0 ? ' class="on"' : ''}></video>`).join('');
const bgv = $$('#hero-bg video'); let ri = 0;
function reelTo(i) { ri = i; $('#reel').style.transform = `translateY(${-i * 1.15}em)`; bgv.forEach((v, k) => { v.classList.toggle('on', k === i); if (k === i) { v.preload = 'auto'; v.play().catch(() => {}); } }); }

/* ---------- Split text for reveals ---------- */
$$('.split').forEach((el) => {
  const walk = (node) => [...node.childNodes].forEach((c) => {
    if (c.nodeType === 3) { const f = document.createDocumentFragment(); c.textContent.split(/(\s+)/).forEach((t) => { if (!t) return; if (/^\s+$/.test(t)) f.appendChild(document.createTextNode(t)); else { const w = document.createElement('span'); w.className = 'w'; w.innerHTML = `<span>${t}</span>`; f.appendChild(w); } }); c.replaceWith(f); }
    else if (c.nodeType === 1 && c.tagName !== 'BR') walk(c);
  });
  walk(el);
});
$$('.giant').forEach((g) => { g.innerHTML = [...g.textContent].map((c) => `<span class="ch">${c}</span>`).join(''); });

/* ---------- Fit the fixed-size UIs into their boxes (they stay vector sharp) ---------- */
function fitAll() {
  const ed = $('#ed-fit'), box = $('#ed-box'); const s = Math.min(box.clientWidth / 1602, box.clientHeight / 902);
  ed.style.transform = `translateX(-50%) scale(${s})`; ed.style.transformOrigin = 'top center';
  const rp = $('#rp-fit'), small = innerWidth < 900; const rs = small ? Math.min(.8, (innerWidth - 40) / 402, (innerHeight * .52) / 640) : Math.min(1.25, (innerHeight * .86) / 640);
  rp.style.transform = `scale(${rs})`; rp.parentElement.style.width = 402 * rs + 'px'; rp.parentElement.style.height = 640 * rs + 'px';
  const hs = Math.min(1.3, innerHeight * .64 / 640, innerWidth * .8 / 400); $('#hero-stage').style.zoom = hs;
}
addEventListener('resize', fitAll); fitAll();

/* ---------- Live clocks in every panel ---------- */
const t0 = performance.now();
setInterval(() => { const s = 1271 + (performance.now() - t0) / 1000; const t = `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`; $$('[data-clock]').forEach((e) => e.textContent = t); $$('[data-mb]').forEach((e) => e.textContent = Math.round(611 + (s - 1271) * .48) + ' MB'); }, 250);
setInterval(() => $$('[data-cpu]').forEach((e) => e.textContent = (1.1 + Math.random() * .7).toFixed(1) + '%'), 1600);

/* ---------- Loader ---------- */
const lb = $('#ld-bar'); for (let i = 0; i < 20; i++) lb.appendChild(document.createElement('i'));
const lbi = $$('i', lb), first = bgv[0]; let shown = 0, target = .1;
const bump = (x) => target = Math.max(target, x);
first.addEventListener('loadedmetadata', () => bump(.6)); first.addEventListener('loadeddata', () => bump(1));
(document.fonts ? document.fonts.ready : Promise.resolve()).then(() => bump(1)); setTimeout(() => bump(1), 2200);
const lt0 = performance.now();
(function tick() {
  const minT = performance.now() - lt0 > (reduce ? 200 : 1500);
  shown += ((minT ? target : Math.min(target, .9)) - shown) * .09; if (target >= 1 && minT && shown > .995) shown = 1;
  const k = Math.round(shown * 20); lbi.forEach((b, i) => { b.classList.toggle('on', i < k); b.classList.toggle('head', i === k - 1); });
  $('#ld-n').textContent = String(Math.round(shown * 100)).padStart(3, '0') + '%';
  if (shown < 1) return requestAnimationFrame(tick);
  setTimeout(start, 200);
})();

function start() {
  $('#ld').classList.add('out'); document.body.classList.remove('lock');
  first.play().catch(() => {});
  if (!reduce) setInterval(() => { if (scrollY < innerHeight * 1.5) reelTo((ri + 1) % reelItems.length); }, 2400);
  if (!window.gsap) return;
  gsap.from('#g-back .ch', { yPercent: 110, opacity: 0, duration: 1.4, stagger: .06, ease: 'expo.out', delay: .4 });
  gsap.from('#g-front .ch', { yPercent: -60, opacity: 0, duration: 1.4, stagger: .06, ease: 'expo.out', delay: .6 });
  gsap.from('#hero-panel', { y: 120, opacity: 0, duration: 1.6, ease: 'expo.out', delay: .5, rotate: -4 });
  if (!reduce && matchMedia('(pointer:fine)').matches) {
    const px = gsap.quickTo('#hero-par', 'x', { duration: .9, ease: 'power3.out' }), py = gsap.quickTo('#hero-par', 'y', { duration: .9, ease: 'power3.out' });
    const gx = gsap.quickTo('#hero .giant', 'x', { duration: 1.2, ease: 'power3.out' }), gy = gsap.quickTo('#hero .giant', 'y', { duration: 1.2, ease: 'power3.out' });
    addEventListener('mousemove', (e) => { if (scrollY > innerHeight * 2) return; const dx = e.clientX / innerWidth - .5, dy = e.clientY / innerHeight - .5; px(Math.round(dx * 28)); py(Math.round(dy * 20)); gx(Math.round(-dx * 14)); gy(Math.round(-dy * 8)); }, { passive: true });
  }
  gsap.from('.hero-tag', { y: 20, opacity: 0, duration: 1.2, ease: 'expo.out', delay: 1 });
}

/* ---------- Scroll story ---------- */
if (!window.gsap) return;
gsap.registerPlugin(ScrollTrigger);
if (window.Lenis && !reduce) {
  const lenis = new Lenis({ duration: 1.15 });
  lenis.on('scroll', ScrollTrigger.update); gsap.ticker.add((t) => lenis.raf(t * 1000)); gsap.ticker.lagSmoothing(0);
  $$('a[href^="#"]').forEach((a) => a.addEventListener('click', (e) => { const el = $(a.getAttribute('href')); if (el) { e.preventDefault(); gsap.to('#jump', { opacity: 1, duration: .22, ease: 'power2.in', overwrite: true, onComplete: () => { lenis.scrollTo(el, { immediate: true, force: true }); ScrollTrigger.update(); gsap.to('#jump', { opacity: 0, duration: .45, delay: .08, ease: 'power2.out' }); } }); } }));
}
const scene = (sel, fn) => ScrollTrigger.create({ trigger: sel, start: 'top top', end: 'bottom bottom', onUpdate: (s) => fn(s.progress), onRefresh: (s) => fn(s.progress) });
const vio = new IntersectionObserver((es) => es.forEach((e) => { const v = e.target; if (e.isIntersecting) { v.preload = 'auto'; v.play().catch(() => {}); } else v.pause(); }), { rootMargin: '-8% 0px' });
const playIn = (sel) => $$('video', $(sel)).forEach((v) => vio.observe(v));

// Hero: the giant words part, the panel lifts flat
gsap.timeline({ scrollTrigger: { trigger: '#hero', start: 'top top', end: 'bottom bottom', scrub: 1 } })
  .to('#g-back', { yPercent: -60, xPercent: -18, opacity: 0, ease: 'none' }, 0)
  .to('#g-front', { yPercent: 70, xPercent: 18, opacity: 0, ease: 'none' }, 0)
  .to('#hero-stage', innerWidth < 820 ? { opacity: 0, yPercent: -80, ease: 'none', duration: .3 } : { left: '72%', ease: 'none' }, 0)
  .to('#hero-panel', { rotate: 0, ease: 'none' }, 0)
  .to('.hero-tag', { opacity: 0, ease: 'none', duration: .2 }, 0)
  .fromTo('#hero-copy', { opacity: 0, y: 40 }, { opacity: 1, y: 0, ease: 'none', duration: .35 }, .3)
  .to('#hero-copy, #hero-stage', { opacity: 0, y: -60, ease: 'none', duration: .25 }, .85);

// The problem: notes drift in, then all fall into the orb
const pains = ['😵 Wait, was that recording?', '🎮 Forgot to hit record. Again.', '⏱️ It only kept 30 seconds.', '🔊 Discord is all over my clip.', '✂️ Now I need a whole editor too.', '🐢 My game drops frames when I record.', '📁 Where did that clip even go?', '🤷 My friends won\'t believe me.', '📦 Discord says the file is too big.'];
const chipBox = $('#chips');
const spots = [[-36, -36, -6], [32, -38, 5], [-42, 30, 4], [38, 28, -5], [-24, 40, -3], [20, 42, 6], [-2, -42, 2], [44, -8, -4], [-44, -6, 3]];
pains.forEach((t, i) => { const c = document.createElement('div'); c.className = 'chip'; const [em, ...rest] = t.split(' '); c.innerHTML = `<i>${em}</i>${rest.join(' ')}`; chipBox.appendChild(c); c.dataset.i = i; });
const chips = $$('.chip');
scene('#problem', (p) => {
  const tIn = ease(seg(p, 0, .14)), gather = ease(seg(p, .4, .66)), out = seg(p, .62, .7);
  $$('#pb-title .w > span').forEach((w, i) => { const k = ease(seg(p, i * .012, .1 + i * .012)); w.style.transform = `translateY(${(1 - k) * 110}%)`; });
  $('#pb-title').style.opacity = 1 - seg(p, .36, .46);
  chips.forEach((c, i) => {
    const [x, y, r] = spots[i]; const appear = ease(seg(p, .1 + i * .025, .22 + i * .025)); const drift = Math.sin(p * 9 + i) * 8;
    const cx = lerp(x * innerWidth / 100, 0, gather), cy = lerp(y * innerHeight / 100 + drift, 0, gather);
    c.style.transform = `translate(-50%,-50%) translate(${cx}px,${cy}px) rotate(${lerp(r, 0, gather)}deg) scale(${lerp(.9 + appear * .1, .1, gather)})`;
    c.style.left = '50%'; c.style.top = '50%'; c.style.opacity = appear * (1 - out);
  });
  const o = $('#pb-orb'); const ok = ease(seg(p, .55, .7)); o.style.opacity = ok; o.style.transform = `translateY(${-ok * 18}vh) scale(${.3 + ok * .7 + Math.sin(p * 40) * .02 * ok})`;
  $$('#pb-exists .w > span').forEach((w, i) => { const k = ease(seg(p, .7 + i * .025, .8 + i * .025)); w.style.transform = `translateY(${(1 - k) * 110}%)`; });
});

// Instant replay: four beats on the real panel
const rp = $('#rp'), beats = $$('.beat'), durs = $('[data-durs]', rp), pill = $('.pill', durs), dspans = $$('span', durs);
const dNames = ['15s', '30s', '1m', '2m'], dPick = [2, 4, 7, 12], flash = $('#flash');
let counted = false;
scene('#replay', (p) => {
  const b = Math.min(3, Math.floor(p * 4)); beats.forEach((el, i) => { const local = seg(p, i / 4, (i + 1) / 4); const vis = i === b ? Math.min(1, local * 6, (1 - local) * 6 + (i === 3 ? 9 : 0)) : 0; el.style.opacity = vis; el.style.transform = `translateY(${(1 - Math.min(1, local * 6)) * 30}px)`; });
  if (b === 0 && !counted) { counted = true; const e = $('[data-count]'); gsap.to({ v: 0 }, { v: 89, duration: 1.6, ease: 'expo.out', onUpdate() { e.textContent = Math.round(this.targets()[0].v); } }); }
  $('#rp-note1').style.opacity = b === 0 ? 1 : 0; $('#rp-note2').style.opacity = b === 1 ? 1 : 0;
  $('[data-strip] .segs', rp).style.setProperty('--buf', (38 + seg(p, 0, .25) * 50) + '%');
  const di = Math.min(3, Math.floor(seg(p, .27, .47) * 4)); pill.style.transform = `translateX(${di * 100}%)`; dspans.forEach((s, i) => s.classList.toggle('on', i === di));
  const saving = p > .6 && p < .645, saved = p >= .645;
  $('[data-savet]', rp).textContent = saving ? 'Saving…' : 'Save last ' + dNames[di];
  $('[data-strip] .pick', rp).style.setProperty('--pick', dPick[di] + '%');
  const kOn = p > .52 && p < .74; const keys = $('#rp-keys'); keys.style.opacity = kOn ? 1 : 0; keys.style.transform = kOn ? 'none' : 'translateY(20px)';
  $('#k-alt').classList.toggle('down', p > .575 && p < .66); $('#k-c').classList.toggle('down', p > .595 && p < .63);
  $('[data-save]', rp).classList.toggle('press', p > .595 && p < .63);
  flash.style.opacity = p > .6 && p < .63 ? (1 - (p - .6) / .03) * .35 : 0;
  $('[data-toast]', rp).classList.toggle('on', saved);
  $('[data-saved]', rp).textContent = saved ? '1' : '0'; $('[data-saveds]', rp).textContent = saved ? '58 MB' : 'No clips yet';
  const sp = ease(seg(p, .78, .86)), dc = ease(seg(p, .88, .97));
  const apps = $$('[data-app]', rp);
  const set = (row, v, off) => { $('.sl', row).style.setProperty('--v', v + '%'); $('.v', row).textContent = off ? 'Off' : Math.round(v * 2) + '%'; row.classList.toggle('off', off); row.lastElementChild.outerHTML = I(off ? 'mute' : 'vol'); };
  set(apps[1], 50 - sp * 50, sp >= 1); set(apps[0], 50 - dc * 30, false);
});

// The editor: six beats on the rebuilt editor
const titles = $$('#ed-titles h2'), ghost = $('#ze-ghost'), ph = $('#ze-ph'), cA = $('#c-a'), cB = $('#c-b'), cF = $('#c-f'), cT = $('#c-title');
const monH = $('#mon-hero'), monF = $('#mon-fighter'), notes = { drag: $('#n-drag'), cut: $('#n-cut'), trim: $('#n-trim'), dbl: $('#n-dbl'), gg: $('#n-gg') };
const place = (el, a, b) => { el.style.display = b > a ? '' : 'none'; el.style.left = a * PX + 'px'; el.style.width = (b - a) * PX + 'px'; };
const tc = (s) => `00:00:${String(Math.floor(s)).padStart(2, '0')}.${String(Math.floor((s % 1) * 1000)).padStart(3, '0')}`;
function insp(html) { const el = $('#ze-insp'); if (el._k !== html) { el._k = html; el.innerHTML = html; } }
const inspEmpty = `<div class="empty"><i>${I('move')}</i><b>Fine-tune your edit</b>Select a timeline clip to adjust timing, motion, sound and effects.</div>`;
const inspClip = (name, sub, start, dur) => `<div class="ze-clip"><i>${I(name === 'GG' ? 'type' : 'film')}</i><div><b>${name}</b><small>${sub}</small></div></div><div class="ze-seg"><span class="on">Video</span><span>Color</span><span>Audio</span><span>Effects</span></div>
  <div class="ze-prop" style="margin-top:8px">Source info ${I('right')}</div><div class="ze-prop">Timing ${I('down')}</div><div class="ze-prop">Timeline start<b>${start}</b></div><div class="ze-prop">Duration<b>${dur}</b></div><div class="ze-prop">Speed<b>100</b></div>`;
scene('#editor', (p) => {
  const st = Math.min(5, Math.floor(p * 6)); titles.forEach((h, i) => { const l = seg(p, i / 6, (i + 1) / 6); h.style.opacity = i === st ? Math.min(1, l * 8, i === 5 ? 1 : (1 - l) * 8) : 0; h.style.transform = `translateY(${(1 - Math.min(1, l * 8)) * 24}px)`; });
  $('#ed-step').innerHTML = `<b>0${st + 1}</b> / 06`;
  const ze = $('#ze'), card0 = $('[data-card="0"]', ze), card1 = $('[data-card="1"]', ze);
  // 1. drag hero.mp4 from the bin onto Video 1
  const d = ease(seg(p, .02, .13)), dropped = p >= .13;
  const g0 = [card0.offsetLeft + 30, card0.offsetTop + 100], laneTop = 44 + 455 + 33 + 40 + 26 + 52 + 10;
  ghost.style.opacity = p > .015 && !dropped ? 1 : 0; ghost.style.left = lerp(g0[0], 190, d) + 'px'; ghost.style.top = lerp(g0[1], laneTop, d) - Math.sin(d * Math.PI) * 120 + 'px';
  card0.classList.toggle('sel', p < .48); card1.classList.toggle('sel', p >= .48 && p < .62);
  notes.drag.style.opacity = p > .01 && p < .14 ? 1 : 0;
  // 2. playhead to 2.7 s, Ctrl+K
  const phTo = ease(seg(p, .18, .25)) * 2.7, split = p >= .27, trimK = ease(seg(p, .36, .45)), bEnd = 6 - trimK * 1.0;
  $('#tool-cut').classList.toggle('hot', p > .2 && p < .32); notes.cut.style.opacity = p > .22 && p < .32 ? 1 : 0;
  const cut = $('#ze-cut'); cut.style.left = 2.7 * PX + 'px'; cut.style.opacity = p > .265 && p < .29 ? 1 : 0;
  // 3. trim the end of the second half
  notes.trim.style.opacity = p > .35 && p < .47 ? 1 : 0;
  // 4. double-click fighter.mp4: it lands after everything
  const added = p >= .53; notes.dbl.style.opacity = p > .49 && p < .6 ? 1 : 0;
  // 5. title
  const ti = ease(seg(p, .66, .72));
  notes.gg.style.opacity = p > .68 && p < .78 ? 1 : 0; $('#tool-title').style.color = p > .64 && p < .74 ? 'var(--acc)' : '';
  // clips
  if (!dropped) { [cA, cB, cF].forEach((c) => c.style.display = 'none'); }
  else if (!split) { place(cA, 0, 6); cB.style.display = 'none'; }
  else { place(cA, 0, 2.7); place(cB, 2.7, bEnd); }
  cB.classList.toggle('trim', p > .36 && p < .46); cB.classList.toggle('sel', split && p < .48); cA.classList.toggle('sel', dropped && !split);
  if (added) { place(cF, bEnd, bEnd + 5); cF.style.transform = `translateY(${(1 - ease(seg(p, .53, .57))) * -30}px)`; cF.classList.toggle('sel', p < .62); } else cF.style.display = 'none';
  if (p >= .66) { place(cT, .8, .8 + 3 * ti); cT.classList.toggle('sel', p < .78); } else cT.style.display = 'none';
  // playhead + playback
  const end = bEnd + 5; let t = dropped ? (p < .27 ? phTo : p < .5 ? 2.7 : p < .62 ? bEnd : p < .78 ? 1.6 : ease(seg(p, .79, .98)) * end) : 0;
  ph.style.left = 180 + t * PX + 'px'; $('#ze-tc').textContent = tc(t); $('#ze-total').textContent = '/ ' + tc(dropped ? (added ? end : split ? bEnd : 6) : 0);
  const showF = added && t >= bEnd; monH.style.opacity = dropped && !showF ? 1 : 0; monF.style.opacity = showF ? 1 : 0; $('#mon-empty').style.opacity = dropped ? 0 : 1;
  const gg = $('#ze-gg'), ggOn = p >= .67 && t >= .8 && t <= 3.8; gg.style.setProperty('--o', ggOn ? 1 : 0); gg.style.setProperty('--s', ggOn ? 1 : .8); gg.style.outlineColor = p < .78 ? 'var(--acc)' : 'transparent';
  $('#ze-export').classList.toggle('glow', p > .96); $('#ze-dirty').textContent = dropped ? 'Unsaved changes' : 'Saved';
  $('#tool-sel').classList.toggle('on', !(p > .2 && p < .32));
  if (!dropped) insp(inspEmpty);
  else if (p >= .66 && p < .78) insp(inspClip('GG', 'Titles · 3 s · 90 frames', '0.800', '3'));
  else if (added && p < .62) insp(inspClip('fighter.mp4', 'Video 1 · 5 s · 150 frames', bEnd.toFixed(3), '5'));
  else if (split) insp(inspClip('hero.mp4', `Video 1 · ${(bEnd - 2.7).toFixed(1)} s`, '2.700', (bEnd - 2.7).toFixed(1)));
  else insp(inspClip('hero.mp4', 'Video 1 · 6 s · 180 frames', '0.000', '6'));
  $('#ze-msg').textContent = !dropped ? 'Imported 4 file(s). Add to timeline to create linked video and audio tracks.' : p < .27 ? 'Added hero.mp4 to Video 1.' : p < .36 ? 'Split at 00:00:02.700.' : p < .5 ? 'Trimmed hero.mp4.' : p < .66 ? 'Added fighter.mp4 after the last clip.' : p < .79 ? 'Created title "GG".' : 'Playing.';
});
['#hero', '#editor'].forEach(playIn);
const gio = new IntersectionObserver((es) => es.forEach((e) => { const v = e.target; if (e.intersectionRatio > .45) { v.preload = 'auto'; v.play().catch(() => {}); } else v.pause(); }), { threshold: [0, .45, 1] });
$$('#gg video').forEach((v) => gio.observe(v));
ScrollTrigger.create({ trigger: '#editor', start: 'top bottom', end: 'bottom top', onToggle: (s) => { [monH, monF, ...$$('.ze-card video')].forEach((v) => { if (s.isActive) { v.preload = 'auto'; v.play().catch(() => {}); } else v.pause(); }); } });

// Export marquee, reveals, proof
gsap.to('#mq-a', { xPercent: -50, ease: 'none', scrollTrigger: { trigger: '#export', start: 'top bottom', end: 'bottom top', scrub: true } });
gsap.fromTo('#mq-b', { xPercent: -50 }, { xPercent: 0, ease: 'none', scrollTrigger: { trigger: '#export', start: 'top bottom', end: 'bottom top', scrub: true } });
gsap.from('#ex-dlg', { y: 100, opacity: 0, duration: 1.4, ease: 'expo.out', scrollTrigger: { trigger: '#export', start: 'top 60%', toggleActions: 'play none none reverse' } });
$$('#export .split, #games .split, #proof .split, #get .split').forEach((h) => gsap.from($$('.w > span', h), { yPercent: 110, duration: 1.2, stagger: .05, ease: 'expo.out', scrollTrigger: { trigger: h, start: 'top 85%', toggleActions: 'play none none reverse' } }));
ScrollTrigger.batch('#gg .g', { start: 'top 92%', onEnter: (b) => gsap.from(b, { y: 60, opacity: 0, scale: .96, stagger: .07, duration: 1.1, ease: 'expo.out' }) });
ScrollTrigger.create({ trigger: '.big-mb', start: 'top 80%', once: true, onEnter: () => gsap.to({ v: 0 }, { v: 89, duration: 2, ease: 'expo.out', onUpdate() { $('#mb').textContent = Math.round(this.targets()[0].v); } }) });
ScrollTrigger.create({ trigger: '#bars', start: 'top 80%', once: true, onEnter: () => $$('#bars .f').forEach((f, i) => gsap.to(f, { width: getComputedStyle(f.closest('.bar')).getPropertyValue('--w'), duration: 1.6, delay: i * .12, ease: 'expo.out' })) });
ScrollTrigger.batch('#vs .c', { start: 'top 90%', onEnter: (b) => gsap.from(b, { y: 60, opacity: 0, rotate: 2, stagger: .08, duration: 1, ease: 'back.out(1.6)' }) });
gsap.fromTo('#get .giant', { xPercent: 10 }, { xPercent: -10, ease: 'none', scrollTrigger: { trigger: '#get', start: 'top bottom', end: 'bottom top', scrub: 1 } });
addEventListener('load', () => { fitAll(); ScrollTrigger.refresh(); });
})();

/* Direct download links from the latest GitHub release; the release page stays as the fallback. */
fetch('https://api.github.com/repos/glitchthekillah/ZenithClipper/releases/latest').then((r) => r.ok ? r.json() : null).then((rel) => {
  if (!rel) return;
  $$('[data-asset]').forEach((a) => { const f = rel.assets.find((x) => x.name.endsWith(a.dataset.asset)); if (f) a.href = f.browser_download_url; });
  $('#dl-ver').textContent = `Version ${rel.tag_name.replace(/^v/, '')}`;
}).catch(() => {});
