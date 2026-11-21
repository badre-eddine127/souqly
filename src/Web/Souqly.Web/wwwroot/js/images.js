// Photos des catégories et des produits (Unsplash, licence libre). Le catalogue ne stocke pas encore d'image :
// on choisit une photo d'après le nom du produit, sinon celle de sa catégorie.

const photo = (id, width = 900) => `https://images.unsplash.com/photo-${id}?auto=format&fit=crop&w=${width}&q=80`;

const P = {
  rug: "1600166898405-da9535204843",
  plates: "1578749556568-bc2c40e68b61",
  argan: "1608571423902-eed4a5ad8108",
  potter: "1493106641515-6b5631de4bb9",
  honey: "1587049352851-8d4e89133924",
  spices: "1596040033229-a9821ebd058d",
  spiceBowls: "1532336414038-cf19250c5757",
  soap: "1600857544200-b2f666a9a2ec",
  soapBars: "1607006344380-b6775a0824a7",
  zellige: "1539020140153-e479b8c22e70",
  kasbah: "1489749798305-4fea3ae63d43",
  tajine: "1541518763669-27fef04b14ea",
  basket: "1590874103328-eac38a683ce7",
  leather: "1473188588951-666fce8e7c68",
  tea: "1576092768241-dec231879fc3",
};

export const HERO_IMAGE = photo(P.rug, 1800);

/** Diaporama plein écran de l'accueil : une diapositive par univers (catégorie ouverte au clic). */
export const HERO_SLIDES = [
  { image: photo(P.kasbah, 2000), eyebrow: "Le savoir-faire du Maroc", title: "Des pièces uniques.<br>Des histoires à partager.", text: "Tapis, poteries, maroquinerie et trésors naturels, façonnés avec passion par les artisans marocains.", category: "" },
  { image: photo(P.rug, 2000), eyebrow: "Tapis et kilims", title: "La laine de l'Atlas,<br>nouée à la main.", text: "Beni Ouarain, Azilal, kilims : des tapis tissés par les femmes des montagnes, chacun unique.", category: "2" },
  { image: photo(P.zellige, 2000), eyebrow: "Artisanat", title: "Zellige, laiton<br>et lumière.", text: "Fontaines, lanternes ciselées, objets de maison : l'art des maâlems de Fès et Marrakech.", category: "1" },
  { image: photo(P.potter, 2000), eyebrow: "Poterie et céramique", title: "La terre de Safi,<br>tournée à la main.", text: "Tajines, bols et assiettes émaillées, cuits au four traditionnel.", category: "3" },
  { image: photo(P.spiceBowls, 2000), eyebrow: "Épicerie fine", title: "Les parfums<br>du souk.", text: "Ras el hanout, miel de thym, thé à la menthe : les saveurs du Maroc livrées chez vous.", category: "7" },
];
export const STORY_IMAGE = photo(P.kasbah, 1200);

// Par identifiant de catégorie (données de référence du service Catalog).
const CATEGORY_PHOTOS = {
  1: P.zellige, // Artisanat
  2: P.rug, // Tapis et kilims
  3: P.potter, // Poterie et céramique
  4: P.leather, // Maroquinerie
  5: P.soap, // Cosmétique naturelle
  6: P.argan, // Huile d'argan
  7: P.spiceBowls, // Épicerie fine
  8: P.basket, // Mode et caftans
};

// Mots du nom du produit → photo (le premier qui correspond l'emporte).
const KEYWORDS = [
  [/tapis|kilim|berb[eè]re|azilal|boucherouite/i, P.rug],
  [/tajine|tagine/i, P.tajine],
  [/assiette|c[eé]ramique|plat|vase/i, P.plates],
  [/bol|poterie|jarre|terre cuite/i, P.potter],
  [/panier|osier|cabas|doum|tress/i, P.basket],
  [/cuir|cartable|babouche|pouf|maroquin|sac/i, P.leather],
  [/argan|amlou/i, P.argan],
  [/savon|beldi|ghassoul/i, P.soap],
  [/rose|cosm[eé]tique/i, P.soapBars],
  [/miel/i, P.honey],
  [/ras el hanout|[eé]pice|safran|cumin|cannelle/i, P.spiceBowls],
  [/th[eé]|menthe|verre/i, P.tea],
  [/zellige|fontaine|lanterne|lampe|laiton|miroir/i, P.zellige],
];

export const categoryImage = (categoryId, width) => photo(CATEGORY_PHOTOS[Number(categoryId)] ?? P.zellige, width);

export function productImage(name, categoryId, width) {
  const match = KEYWORDS.find(([re]) => re.test(String(name)));
  return match ? photo(match[1], width) : categoryImage(categoryId, width);
}
