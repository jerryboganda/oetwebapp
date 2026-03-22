import { FileCloud, FileX, Ticket } from "phosphor-react";
import { ClockCountdown } from "@phosphor-icons/react";

export const ticketsAppDatas = [
  {
    count: 185,
    title: "All Tickets",
    bgColor: "light-primary",
    icon: Ticket,
    textColor: "primary-dark",
    avatars: [
      {
        img: "/images/avatar/4.png",
        tooltip: "Sabrina Torres",
        bgColor: "danger",
      },
      {
        img: "/images/avatar/1.png",
        tooltip: "Sabrina Torres",
        bgColor: "success",
      },
      {
        img: "/images/avatar/2.png",
        tooltip: "Sabrina Torres",
        bgColor: "warning",
      },
      {
        img: "/images/avatar/3.png",
        tooltip: "Sabrina Torres",
        bgColor: "info",
      },
    ],
    extraCount: 5,
  },
  {
    count: 185,
    title: "Pending Tickets",
    bgColor: "light-info",
    icon: ClockCountdown,
    textColor: "info-dark",
    avatars: [
      {
        img: "/images/avatar/4.png",
        tooltip: "Sabrina Torres",
        bgColor: "danger",
      },
      {
        img: "/images/avatar/1.png",
        tooltip: "Sabrina Torres",
        bgColor: "success",
      },
      {
        img: "/images/avatar/2.png",
        tooltip: "Sabrina Torres",
        bgColor: "warning",
      },
      {
        img: "/images/avatar/3.png",
        tooltip: "Sabrina Torres",
        bgColor: "info",
      },
    ],
    extraCount: 5,
  },
  {
    count: 185,
    title: "Completed Tickets",
    bgColor: "light-success",
    icon: FileCloud,
    textColor: "success-dark",
    avatars: [
      {
        img: "/images/avatar/4.png",
        tooltip: "Sabrina Torres",
        bgColor: "danger",
      },
      {
        img: "/images/avatar/1.png",
        tooltip: "Sabrina Torres",
        bgColor: "success",
      },
      {
        img: "/images/avatar/2.png",
        tooltip: "Sabrina Torres",
        bgColor: "warning",
      },
      {
        img: "/images/avatar/3.png",
        tooltip: "Sabrina Torres",
        bgColor: "info",
      },
    ],
    extraCount: 5,
  },
  {
    count: 185,
    title: "Cancelled Tickets",
    bgColor: "light-warning",
    textColor: "warning-dark",
    icon: FileX,
    avatars: [
      {
        img: "/images/avatar/4.png",
        tooltip: "Sabrina Torres",
        bgColor: "danger",
      },
      {
        img: "/images/avatar/1.png",
        tooltip: "Sabrina Torres",
        bgColor: "success",
      },
      {
        img: "/images/avatar/2.png",
        tooltip: "Sabrina Torres",
        bgColor: "warning",
      },
      {
        img: "/images/avatar/3.png",
        tooltip: "Sabrina Torres",
        bgColor: "info",
      },
    ],
    extraCount: 5,
  },
];

export const ticketstable = [
  {
    id: "AR 2044",
    agent: "Gavin Cortez",
    priority: { label: "Medium", color: "warning" },
    title: "Bug Report",
    status: { label: "in progress", color: "success" },
    date: "1 Jan 2024",
    dueDate: "3 Feb 2024",
  },
  {
    id: "AR 2045",
    agent: "Emily Johnson",
    priority: { label: "High", color: "success" },
    title: "Feature Request",
    status: { label: "open", color: "primary" },
    date: "2 Jan 2024",
    dueDate: "5 Feb 2024",
  },
  {
    id: "AR 7452",
    agent: "Gavin Joyce",
    priority: { label: "High", color: "success" },
    title: "Performance Issue",
    status: { label: "in progress", color: "success" },
    date: "14 Jan 2024",
    dueDate: "30 Jan 2024",
  },
  {
    id: "AR 1023",
    agent: "Gloria Little",
    priority: { label: "Medium", color: "warning" },
    title: "Security Concern",
    status: { label: "open", color: "primary" },
    date: "6 hours ago",
    dueDate: "12 Feb 2024",
  },
  {
    id: "AR 2305",
    agent: "Jena Gaines",
    priority: { label: "High", color: "success" },
    title: "User Access/Permissions",
    status: { label: "closed", color: "info" },
    date: "6 hours ago",
    dueDate: "16 Jan 2024",
  },
  {
    id: "AR 2058",
    agent: "Jenette Caldwell",
    priority: { label: "Lower", color: "danger" },
    title: "System Outage or Downtime",
    status: { label: "open", color: "primary" },
    date: "20 Jan 2024",
    dueDate: "21 Feb 2024",
  },
  {
    id: "AR 1935",
    agent: "Jennifer Acosta",
    priority: { label: "Medium", color: "warning" },
    title: "Data Issue",
    status: { label: "open", color: "primary" },
    date: "7 Jun 2024",
    dueDate: "8 Jul 2024",
  },
  {
    id: "AR 3056",
    agent: "Jennifer Chang",
    priority: { label: "High", color: "success" },
    title: "Integration Issue",
    status: { label: "in progress", color: "success" },
    date: "10 hours ago",
    dueDate: "7 Aug 2024",
  },
  {
    id: "AR 0358",
    agent: "Michael Silva",
    priority: { label: "Medium", color: "warning" },
    title: "User Interface Issue",
    status: { label: "open", color: "primary" },
    date: "14 Jun 2024",
    dueDate: "16 Feb 2024",
  },
  {
    id: "AR 4590",
    agent: "Michelle House",
    priority: { label: "Lower", color: "danger" },
    title: "General Inquiry or Request",
    status: { label: "closed", color: "info" },
    date: "4 Jul 2024",
    dueDate: "5 Aug 2024",
  },
];

export const ticketDetails = [
  { label: "Ticket Number", value: "AR 2044" },
  { label: "Client", value: "Gavin Cortez" },
  { label: "Priority", value: "Medium" },
  { label: "Title", value: "Bug Report" },
  { label: "Status", value: "In Progress" },
  { label: "Create Date", value: "1 Jan 2024" },
  { label: "Due Date", value: "3 Feb 2024" },
];
