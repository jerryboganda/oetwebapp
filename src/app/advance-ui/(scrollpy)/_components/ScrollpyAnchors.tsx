import React, { useRef, useState } from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";

const ScrollpyAnchors: React.FC = () => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [_activeId, setActiveId] = useState("simple-list-item-1");
  const itemRefs = useRef<(HTMLHeadingElement | null)[]>([]);

  React.useEffect(() => {
    itemRefs.current = itemRefs.current.slice(0, 5);
  }, []);

  const handleScroll = () => {
    if (!scrollRef.current) return;

    const scrollTop = scrollRef.current.scrollTop;

    for (let i = 0; i < itemRefs.current.length; i++) {
      const item = itemRefs.current[i];
      if (!item) continue;

      const offsetTop = item.offsetTop;
      const offsetHeight = item.offsetHeight;

      if (
        scrollTop + 100 >= offsetTop &&
        scrollTop + 100 < offsetTop + offsetHeight
      ) {
        setActiveId(item.id);
        break;
      }
    }
  };

  const addItemRef = (el: HTMLHeadingElement | null, index: number) => {
    itemRefs.current[index] = el;
  };

  return (
    <Col xs="12">
      <Row>
        <Col sm={4}>
          <Card>
            <CardHeader>
              <h5>Simple anchors</h5>
            </CardHeader>
            <CardBody>
              <div
                id="simple-list-example"
                className="d-flex flex-column gap-2 simple-list-example-scrollspy text-center"
              >
                <a className="p-1 rounded border" href="#simple-list-item-1">
                  Item 1
                </a>
                <a className="p-1 rounded border" href="#simple-list-item-2">
                  Item 2
                </a>
                <a className="p-1 rounded border" href="#simple-list-item-3">
                  Item 3
                </a>
                <a className="p-1 rounded border" href="#simple-list-item-4">
                  Item 4
                </a>
                <a className="p-1 rounded border" href="#simple-list-item-5">
                  Item 5
                </a>
              </div>
            </CardBody>
          </Card>
        </Col>

        <div className="col-sm-8">
          <Card>
            <CardBody>
              <div
                ref={scrollRef}
                onScroll={handleScroll}
                className="scrollspy-example h-245 overflow-y-scroll app-scroll"
              >
                <h5
                  id="simple-list-item-1"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 0)}
                >
                  Item 1
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  Are you looking for a creative professional who can bring your
                  website to life? Look no further than a web designer! With
                  their expertise in both design and technology, web designers
                  have the skills to create visually stunning and user-friendly
                  websites that will captivate your audience. They understand
                  the importance of a well-designed website in today&#39;s
                  digital age and can tailor their designs to match your brand
                  identity and target audience.
                </p>

                <h5
                  id="simple-list-item-2"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 1)}
                >
                  Item 2
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  A web designer is not just someone who knows how to make a
                  website look good. They are skilled in various programming
                  languages and have a deep understanding of user experience and
                  interface design. They can create a website that not only
                  looks visually appealing but also functions seamlessly across
                  different devices and browsers. From choosing the right color
                  palette to creating intuitive navigation, a web designer pays
                  attention to every detail to ensure your website is both
                  aesthetically pleasing and user-friendly.
                </p>

                <h5
                  id="simple-list-item-3"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 2)}
                >
                  Item 3
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  It is a long-established fact that a reader will be distracted
                  by the readable content of a page when looking at its layout.
                  The point of using Lorem Ipsum is that it has a more-or-less
                  normal distribution of letters, as opposed to using
                  &#39;Content here, content here&#39;, making it look like
                  readable English. Many desktop publishing packages and web
                  page editors now use Lorem Ipsum as their default model text,
                  and a search for &#39;lorem ipsum&#39; will uncover many
                  websites still in their infancy. Various versions have evolved
                  over the years, sometimes by accident, sometimes on purpose
                  injected humour and the like
                </p>

                <h5
                  id="simple-list-item-4"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 3)}
                >
                  Item 4
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  {" "}
                  In addition to their technical skills, web designers are also
                  great problem solvers. They can analyze your business goals
                  and translate them into a website that not only meets your
                  needs but also exceeds your expectations. They are constantly
                  staying updated with the latest design trends and technologies
                  to ensure your website is modern and competitive in the online
                  landscape. A web designer is a valuable asset to any business
                  or individual looking to establish a strong online presence
                  and make a lasting impression on their audience.
                </p>

                <h5
                  id="simple-list-item-5"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 4)}
                >
                  Item 5
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  {" "}
                  A web designer is a professional who specializes in creating
                  and designing websites. They possess a unique skill set that
                  combines creativity, technical knowledge, and an understanding
                  of user experience. Web designers are responsible for the
                  visual layout, functionality, and overall aesthetics of a
                  website. They work closely with clients to understand their
                  needs and objectives, and then translate those into a visually
                  appealing and user-friendly website.
                </p>
              </div>
            </CardBody>
          </Card>
        </div>
      </Row>
    </Col>
  );
};
export default ScrollpyAnchors;
