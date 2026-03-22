import React from "react";
import { Card, CardBody, CardHeader, CardFooter, Col } from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

type CardData = {
  id: string;
  title: string;
  headerText?: string;
  body: string;
  footer?: string;
  className?: string;
};

const cardsData: CardData[] = [
  {
    id: "cardheader1",
    title: "Card Header",
    headerText: "Card Body",
    body: "With supporting text below as a natural lead-in to additional content.",
  },
  {
    id: "cardheader2",
    title: "",
    headerText: "Card Body",
    body: "With supporting text below as a natural lead-in to additional content.",
    footer: "Card Footer",
  },
  {
    id: "cardheader3",
    title: "Card Header",
    body: "With supporting text below as a natural lead-in to additional content below as a natural.",
    footer: "Card Footer",
    className: "border-0",
  },
  {
    id: "cardheader4",
    title: "Hover Effect",
    headerText: "Card Body",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect",
  },
  {
    id: "cardheader5",
    title: "Primary card",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-primary",
  },
  {
    id: "cardheader6",
    title: "Secondary card",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-secondary",
  },
  {
    id: "cardheader7",
    title: "Success card",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-success",
  },
  {
    id: "cardheader8",
    title: "Danger card",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-danger",
  },
  {
    id: "cardheader9",
    title: "Outline card",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-outline-primary",
  },
  {
    id: "cardheader10",
    title: "Secondary",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-outline-secondary",
  },
  {
    id: "cardheader12",
    title: "Light card",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-light-primary",
  },
  {
    id: "cardheader11",
    title: "Secondary",
    body: "With supporting text below lead-in to additional content below as a natural.",
    className: "hover-effect card-light-secondary",
  },
];

const BasicCards: React.FC = () => {
  return (
    <>
      {cardsData.map(
        ({ id, title, headerText, body, footer, className }, index) => {
          const togglerId = `toggler-${id}`;
          return (
            <Col md="6" xl="3" key={id}>
              <Card className={className || "card"}>
                <CardHeader className="code-header d-flex justify-content-between align-items-center">
                  <h5>{title}</h5>
                  <span id={togglerId} role="button">
                    <IconCode
                      data-source={`card${index + 1}`}
                      className="source"
                      size={32}
                    />
                  </span>
                </CardHeader>

                <CardBody>
                  {headerText && <h6>{headerText}</h6>}
                  <p>{body}</p>
                </CardBody>

                {footer && (
                  <CardFooter>
                    <h5>{footer}</h5>
                  </CardFooter>
                )}
              </Card>

              <UncontrolledCollapseWrapper toggler={`#${togglerId}`}>
                <pre className={`card${index + 1} mt-3`}>
                  <code className="language-html">
                    {`<div class="card-body">
  <h6>Card body</h6>
  <p>...</p>
</div>`}
                  </code>
                </pre>
              </UncontrolledCollapseWrapper>
            </Col>
          );
        }
      )}
    </>
  );
};

export default BasicCards;
