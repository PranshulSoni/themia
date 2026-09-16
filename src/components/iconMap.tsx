// The shared pick-an-icon catalogue.
//
// Lives apart from `IconPicker.tsx` because the two have very different reach:
// the picker is a config-window control (portal, popup layer, keyboard grid),
// while the MAP is read by surfaces that only ever render an icon — the view
// bar, the group widget, the view switcher, and the tray sync in every overlay
// window. Importing the map used to drag the whole picker into those bundles.
//
// `IconPicker.tsx` re-exports this, so every existing importer keeps working and
// there is still exactly one definition of the catalogue.

import type { JSX } from 'solid-js';
import {
  Home, Monitor, Gamepad2, Briefcase, Music, Image, Code, Globe, Mail, MessageSquare,
  Calendar, Film, BookOpen, Heart, Zap, Coffee, Star, Settings, Users, Palette,
  Camera, Cloud, Database, Folder, Gift, Headphones, Key, Layers, Map as MapIcon,
  Phone, Rocket, Shield, ShoppingCart, Terminal, Tv, Umbrella, Wifi, Wrench,
  Bike, Car, Plane, Trophy, Sparkles,
} from 'lucide-solid';

// SCREEN_ICON_MAP retained as a temporary alias during the screen → view rename.
export const VIEW_ICON_MAP: Record<string, (size: number) => JSX.Element> = {
  home:      (s) => <Home size={s} strokeWidth={1.5} />,
  monitor:   (s) => <Monitor size={s} strokeWidth={1.5} />,
  gamepad:   (s) => <Gamepad2 size={s} strokeWidth={1.5} />,
  briefcase: (s) => <Briefcase size={s} strokeWidth={1.5} />,
  music:     (s) => <Music size={s} strokeWidth={1.5} />,
  image:     (s) => <Image size={s} strokeWidth={1.5} />,
  code:      (s) => <Code size={s} strokeWidth={1.5} />,
  globe:     (s) => <Globe size={s} strokeWidth={1.5} />,
  mail:      (s) => <Mail size={s} strokeWidth={1.5} />,
  message:   (s) => <MessageSquare size={s} strokeWidth={1.5} />,
  calendar:  (s) => <Calendar size={s} strokeWidth={1.5} />,
  film:      (s) => <Film size={s} strokeWidth={1.5} />,
  book:      (s) => <BookOpen size={s} strokeWidth={1.5} />,
  heart:     (s) => <Heart size={s} strokeWidth={1.5} />,
  zap:       (s) => <Zap size={s} strokeWidth={1.5} />,
  coffee:    (s) => <Coffee size={s} strokeWidth={1.5} />,
  star:      (s) => <Star size={s} strokeWidth={1.5} />,
  settings:  (s) => <Settings size={s} strokeWidth={1.5} />,
  users:     (s) => <Users size={s} strokeWidth={1.5} />,
  palette:   (s) => <Palette size={s} strokeWidth={1.5} />,
  camera:    (s) => <Camera size={s} strokeWidth={1.5} />,
  cloud:     (s) => <Cloud size={s} strokeWidth={1.5} />,
  database:  (s) => <Database size={s} strokeWidth={1.5} />,
  folder:    (s) => <Folder size={s} strokeWidth={1.5} />,
  gift:      (s) => <Gift size={s} strokeWidth={1.5} />,
  headphones:(s) => <Headphones size={s} strokeWidth={1.5} />,
  key:       (s) => <Key size={s} strokeWidth={1.5} />,
  layers:    (s) => <Layers size={s} strokeWidth={1.5} />,
  map:       (s) => <MapIcon size={s} strokeWidth={1.5} />,
  phone:     (s) => <Phone size={s} strokeWidth={1.5} />,
  rocket:    (s) => <Rocket size={s} strokeWidth={1.5} />,
  shield:    (s) => <Shield size={s} strokeWidth={1.5} />,
  cart:      (s) => <ShoppingCart size={s} strokeWidth={1.5} />,
  terminal:  (s) => <Terminal size={s} strokeWidth={1.5} />,
  tv:        (s) => <Tv size={s} strokeWidth={1.5} />,
  umbrella:  (s) => <Umbrella size={s} strokeWidth={1.5} />,
  wifi:      (s) => <Wifi size={s} strokeWidth={1.5} />,
  wrench:    (s) => <Wrench size={s} strokeWidth={1.5} />,
  bike:      (s) => <Bike size={s} strokeWidth={1.5} />,
  car:       (s) => <Car size={s} strokeWidth={1.5} />,
  plane:     (s) => <Plane size={s} strokeWidth={1.5} />,
  trophy:    (s) => <Trophy size={s} strokeWidth={1.5} />,
  sparkles:  (s) => <Sparkles size={s} strokeWidth={1.5} />,
};
export const SCREEN_ICON_MAP = VIEW_ICON_MAP;

/** Is this a catalogue key? The one membership test, so a persisted icon name
 *  is validated the same way everywhere it is read. */
export function isKnownIcon(key: string): boolean {
  return Object.prototype.hasOwnProperty.call(VIEW_ICON_MAP, key);
}
