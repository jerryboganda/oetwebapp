"use client";
import { useState } from "react";
import { Container, Row } from "react-bootstrap";
import "datatables.net-dt/css/dataTables.dataTables.min.css";
import { IconStack } from "@tabler/icons-react";
import { CheckCircle, UserCircleGear } from "phosphor-react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import ApiTopCards from "./ApiTopCards";
import ApiTableCards from "./ApiTableCards";
export interface ApiCard {
  title: string;
  value: string;
  percentage?: string;
  subtitle?: string;
  bgColor: string;
  icon: JSX.Element;
  avatars?: { src: string; bgColor?: string }[];
  showButton?: boolean;
}

export const apiCardsData: ApiCard[] = [
  {
    title: "Total Users",
    value: "2564k",
    percentage: "92.9%",
    subtitle: "users income",
    bgColor: "bg-light-primary",
    icon: <UserCircleGear size={20} />,
  },
  {
    title: "Members",
    value: "1210k",
    percentage: "15.5%",
    subtitle: "members income",
    bgColor: "bg-light-success",
    icon: <UserCircleGear size={20} />,
  },
  {
    title: "Active Now",
    value: "416k",
    bgColor: "bg-light-danger",
    icon: <CheckCircle size={20} />,
    avatars: [
      { src: "/images/avatar/4.png", bgColor: "text-bg-danger" },
      { src: "/images/avatar/1.png", bgColor: "text-bg-success" },
      { src: "/images/avatar/2.png", bgColor: "text-bg-primary" },
      { src: "/images/avatar/3.png", bgColor: "text-bg-warning" },
    ],
  },
  {
    title: "Developer",
    value: "64",
    bgColor: "bg-light-warning",
    icon: <CheckCircle size={20} />,
    showButton: true,
  },
];
export const apiKeyData = [
  {
    id: 1,
    name: "Gavin Cortez",
    parentName: "Patty OFurniture",
    key: "12e8d619-5ab4-41db-b574-eb727f82d836",
    date: "1 Jan 2024",
    email: "cristipola@gmailod.com",
    avatar: "/images/avatar/1.png",
    bg: "text-bg-primary",
  },
  {
    id: 2,
    name: "Martena Mccray",
    parentName: "Olive Yew",
    key: "b589bd39-afa6-4dc2-a0e6-ee286d429689",
    date: "8 Jan 2024",
    email: "jiji4fast@trumvia.online",
    avatar: "/images/avatar/14.png",
    bg: "text-bg-dark",
  },
  {
    id: 3,
    name: "Colby Newton",
    parentName: "Al Beback",
    key: "caa2ef43-904a-4561-9eb8-3b745f1cfaae",
    date: "12 Jan 2024",
    email: "colbynewton@nomail.com",
    avatar: "/images/avatar/5.png",
    bg: "text-bg-warning",
  },
  {
    id: 4,
    name: "Dana Wooten",
    parentName: "Sue Flay",
    key: "f4ef9d8d-b674-4776-a6d5-4c82e1aa5a3c",
    date: "18 Jan 2024",
    email: "dana@wootenemail.com",
    avatar: "/images/avatar/8.png",
    bg: "text-bg-info",
  },
  {
    id: 5,
    name: "Jonas Bentley",
    parentName: "Anita Bath",
    key: "9c5db4c2-c718-49c2-94e2-1d75f3b2b78d",
    date: "21 Jan 2024",
    email: "jonasb@securemail.net",
    avatar: "/images/avatar/10.png",
    bg: "text-bg-success",
  },
  {
    id: 6,
    name: "Felicity Poole",
    parentName: "Rick O'Shea",
    key: "d3a5bb7e-7f59-4d2d-aecd-57e4c91b7201",
    date: "26 Jan 2024",
    email: "felicity.poole@example.org",
    avatar: "/images/avatar/12.png",
    bg: "text-bg-danger",
  },
  {
    id: 7,
    name: "Marlon Bates",
    parentName: "Barb Dwyer",
    key: "a8b1e2f3-b1e7-4e60-bcd5-d93c4e3a3caa",
    date: "30 Jan 2024",
    email: "marlonb@webmail.biz",
    avatar: "/images/avatar/15.png",
    bg: "text-bg-secondary",
  },
];
const ApiPage = () => {
  const [apiKeyList, setApiKeyList] = useState(apiKeyData);

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Api"
        title="Apps"
        path={["Api"]}
        Icon={IconStack}
      />
      <Row>
        <ApiTopCards apiKeyList={apiKeyList} setApiKeyList={setApiKeyList} />
        <ApiTableCards apiKeyList={apiKeyList} setApiKeyList={setApiKeyList} />
      </Row>
    </Container>
  );
};

export default ApiPage;
