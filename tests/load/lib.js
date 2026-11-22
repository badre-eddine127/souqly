// Petites aides sans dépendance réseau (k6 n'a pas besoin de télécharger jslib).

export function randomItem(items) {
  return items[Math.floor(Math.random() * items.length)];
}

export function uuidv4() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}
