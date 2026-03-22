import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Col,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  UncontrolledCollapse,
} from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import classnames from "classnames";

type TabItem = {
  id: string;
  title: string;
  content: string[];
};

const tabs: TabItem[] = [
  {
    id: "connect",
    title: "Connect",
    content: [
      "This field is a rich HTML field with a content editor like others used in Sitefinity. It accepts images, video, tables, text, etc. Street art polaroid microdosing la croix taxidermy. Jean shorts kinfolk distillery lumbersexual pinterest XOXO semiotics.",
    ],
  },
  {
    id: "discover",
    title: "Discover",
    content: [
      "This field is a rich HTML field with a content editor like others used in Sitefinity. It accepts images, video, tables, text, etc. Street art polaroid microdosing la croix taxidermy. Jean shorts kinfolk distillery lumbersexual pinterest XOXO semiotics.",
    ],
  },
  {
    id: "order",
    title: "Orders",
    content: [
      "This field is a rich HTML field with a content editor like others used in Sitefinity. It accepts images, video, tables, text, etc. Street art polaroid microdosing la croix taxidermy. Jean shorts kinfolk distillery lumbersexual pinterest XOXO semiotics.",
    ],
  },
];

const OutlineTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>(tabs[0]?.id || "");

  return (
    <Col lg={6}>
      <Card>
        <CardHeader className="code-header">
          <h5>Outline Tabs</h5>
          <a href="#" id="togglerOutlineTabBtn">
            <IconCode data-source="av2" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <Nav tabs className="tab-outline-primary" id="Outline" role="tablist">
            {tabs.map((tab) => (
              <NavItem key={tab.id}>
                <NavLink
                  className={classnames({ active: activeTab === tab.id })}
                  onClick={() => setActiveTab(tab.id)}
                  id={`${tab.id}-tab`}
                  type="button"
                  role="tab"
                  aria-controls={`${tab.id}-tab-pane`}
                  aria-selected={activeTab === tab.id ? "true" : "false"}
                >
                  {tab.title}
                </NavLink>
              </NavItem>
            ))}
          </Nav>

          <TabContent
            activeTab={activeTab}
            className="mt-3"
            id="OutlineContent"
          >
            {tabs.map((tab) => (
              <TabPane
                key={tab.id}
                tabId={tab.id}
                role="tabpanel"
                id={`${tab.id}-tab-pane`}
              >
                {tab.content.map((paragraph, idx) => (
                  <p key={idx}>{paragraph}</p>
                ))}
              </TabPane>
            ))}
          </TabContent>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerOutlineTabBtn">
          <pre className="mt-3">
            <code className="language-html">{`<Card>
  <CardHeader className="d-flex justify-content-between align-items-center">
    <h5>Outline Tabs</h5>
    <a href="#tab2" aria-expanded="false" aria-controls="tab2">
      <IconCode data-source="t2" className="source" size={32} />
    </a>
  </CardHeader>
  <CardBody>
    <Nav tabs className="tab-outline-primary" id="Outline" role="tablist">
${tabs
  .map(
    (tab) => `      <NavItem role="presentation">
        <NavLink
          ${tab.id === "connect" ? 'className="active"' : ""}
          id="${tab.id}-tab"
          role="tab"
          aria-controls="${tab.id}-tab-pane"
          aria-selected="${tab.id === "connect" ? "true" : "false"}"
        >
          ${tab.title}
        </NavLink>
      </NavItem>`
  )
  .join("\n")}
    </Nav>
    <TabContent className="mt-3" activeTab="outline" id="OutlineContent">
${tabs
  .map(
    (tab) => `      <TabPane
        tabId="${tab.id}"
        role="tabpanel"
        id="${tab.id}-tab-pane"
        className="${tab.id === "connect" ? "show active" : ""}"
      >
        <h6 className="mb-1">This is the content of ${tab.title.toLowerCase()}.</h6>
${tab.content.map((c) => `        <p>${c}</p>`).join("\n")}
      </TabPane>`
  )
  .join("\n")}
    </TabContent>
  </CardBody>
</Card>`}</code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};

export default OutlineTabs;
