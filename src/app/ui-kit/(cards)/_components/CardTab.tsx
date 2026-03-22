"use client";
import React, { useState } from "react";
import {
  Card,
  CardBody,
  CardImg,
  CardText,
  CardTitle,
  Col,
  Nav,
  NavItem,
  NavLink,
  Row,
  TabContent,
  TabPane,
} from "reactstrap";
import {
  IconDisc,
  IconFileX,
  IconHistory,
  IconKeyboardShow,
  IconLifebuoy,
  IconStar,
} from "@tabler/icons-react";

type TabData = {
  id: string;
  label: string;
  icon: React.ReactNode;
  content: React.ReactNode;
};

const firstTabData: TabData[] = [
  {
    id: "description",
    label: "Description",
    icon: <IconLifebuoy size={18} className="pe-1" />,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudo class to show tabs, use
        anchors with fragment identifiers to switch between them.
      </p>
    ),
  },
  {
    id: "howitworks",
    label: "How It Works",
    icon: <IconKeyboardShow size={24} className="pe-1" />,
    content: (
      <ol>
        <li>Show only the last tab.</li>
        <li>
          If <code>:target</code> matches a tab, show it and hide all following
          siblings.
        </li>
        <li>Matches a tab, show it and hide all following siblings.</li>
      </ol>
    ),
  },
  {
    id: "drawbacks",
    label: "Drawbacks",
    icon: <IconFileX size={24} className="pe-1" />,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudo class to show tabs, use
        anchors with fragment identifiers to switch between them.
      </p>
    ),
  },
];

const secondTabData: TabData[] = [
  {
    id: "features",
    label: "Features",
    icon: <IconDisc size={24} className="pe-1" />,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudo class to show tabs, use
        anchors with fragment identifiers to switch between them.
      </p>
    ),
  },
  {
    id: "history",
    label: "History",
    icon: <IconHistory size={24} className="pe-1" />,
    content: (
      <ol>
        <li>Show only the last tab.</li>
        <li>
          If <code>:target</code> matches a tab, show it and hide all following
          siblings.
        </li>
        <li>Matches a tab, show it and hide all following siblings.</li>
      </ol>
    ),
  },
  {
    id: "reviews",
    label: "Reviews",
    icon: <IconStar size={24} className="pe-1" />,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudo class to show tabs, use
        anchors with fragment identifiers to switch between them.
      </p>
    ),
  },
];

const RenderReactstrapTabs = ({
  data,
  tabId,
}: {
  data: TabData[];
  tabId: string;
}) => {
  const [activeTab, setActiveTab] = useState<string>(data[0]?.id || "");

  return (
    <Card className="hover-effect equal-card tab-card">
      <CardBody>
        <Nav
          tabs
          className={
            tabId === "bgContent"
              ? "tab-primary bg-primary p-1"
              : "tab-light-primary"
          }
        >
          {data.map(({ id, label, icon }) => (
            <NavItem key={id}>
              <NavLink
                className={activeTab === id ? "active" : ""}
                onClick={() => setActiveTab(id)}
                role="tab"
              >
                {icon} {label}
              </NavLink>
            </NavItem>
          ))}
        </Nav>

        <TabContent activeTab={activeTab} className="pt-3">
          {data.map(({ id, content }) => (
            <TabPane tabId={id} key={id} role="tabpanel">
              {content}
            </TabPane>
          ))}
        </TabContent>
      </CardBody>
    </Card>
  );
};

const CardTab: React.FC = () => {
  return (
    <>
      <Col xl="6">
        <RenderReactstrapTabs data={firstTabData} tabId="LightContent" />
      </Col>

      <Col xl="6">
        <Card className="mb-3 hover-effect">
          <Row>
            <Col md="6" xl="8">
              <CardBody>
                <CardTitle tag="h5">Card Title</CardTitle>
                <CardText>
                  This is a wider card with supporting text with supporting
                  little bit longer below as a natural lead-in to additional
                  content. This content is a little bit longer.
                </CardText>
                <CardText>
                  <small className="text-body-secondary">
                    Last updated 3 min&#39;s ago
                  </small>
                </CardText>
              </CardBody>
            </Col>
            <Col md="6" xl="4">
              <CardImg
                src="/images/blog/09.jpg"
                className="img-fluid"
                alt="..."
              />
            </Col>
          </Row>
        </Card>
      </Col>

      <Col xl="6">
        <RenderReactstrapTabs data={secondTabData} tabId="bgContent" />
      </Col>
    </>
  );
};

export default CardTab;
